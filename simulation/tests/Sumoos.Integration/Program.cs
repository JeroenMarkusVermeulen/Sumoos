using System.Diagnostics;
using Sumoos.Controller.Communication;
using Sumoos.Controller.Configuration;
using Sumoos.Controller.Services;

var gateway=new GatewaySession();
using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(90));
var run=gateway.RunAsync(new AppOptions { BrokerHost="127.0.0.1", BrokerPort=1884 },true,null,null,cancellation.Token,true);
int inputLeft=0,inputRight=0;
bool inputEnabled=true;
var feeder=Task.Run(async () => {
    while(!cancellation.IsCancellationRequested) {
        if(Volatile.Read(ref inputEnabled)) gateway.FeedSimulation(Volatile.Read(ref inputLeft),Volatile.Read(ref inputRight));
        try { await Task.Delay(20,cancellation.Token); } catch(OperationCanceledException) { break; }
    }
});
async Task Wait(Func<bool> condition, int l=0,int r=0, bool feed=true) {
    Volatile.Write(ref inputEnabled,feed);
    Volatile.Write(ref inputLeft,l); Volatile.Write(ref inputRight,r);
    var until=Environment.TickCount64+15000;
    while(!condition()) {
        if(Environment.TickCount64>until)
            throw new Exception("Timed out: "+gateway.Snapshot.Reason+" "+gateway.Snapshot.Feedback+
                Environment.NewLine+string.Join(Environment.NewLine, gateway.Logs.TakeLast(12).Select(entry => entry.Message)));
        if(feed) gateway.FeedSimulation(l,r);
        await Task.Delay(20);
    }
}
bool Ready() {
    return gateway.Snapshot.Feedback.State == FeedbackState.Ready;
}
bool RobotMoving() {
    return gateway.Snapshot.Feedback.Report?.Left == 15;
}
async Task Docker(string command) {
    var start=new ProcessStartInfo("docker") { RedirectStandardOutput=true,RedirectStandardError=true };
    foreach(var arg in ("compose -f simulation/compose.yaml "+command).Split(' '))start.ArgumentList.Add(arg);
    using var p=Process.Start(start)!;
    var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
    await p.WaitForExitAsync();await stdout;
    if(p.ExitCode!=0)throw new Exception(await stderr);
}
try {
    await Wait(Ready);
    gateway.ArmSimulation();
    await Wait(()=>gateway.Snapshot.Armed);
    await Wait(RobotMoving,50,50);
    Console.WriteLine("PASS dashboard drives robot through real MQTT and receives motor feedback");
    await Wait(()=>!gateway.Snapshot.Armed,feed:false);
    if(gateway.Snapshot.Left!=0)throw new Exception("Input-loss motion");
    Console.WriteLine("PASS input heartbeat loss stops");
    foreach(var component in new[]{"robot","broker"}) {
        await Wait(Ready);
        gateway.FeedSimulation(0,0);gateway.ArmSimulation();
        await Wait(()=>gateway.Snapshot.Armed);
        await Wait(RobotMoving,50,50);
        await Docker("stop "+component);
        await Wait(()=>!gateway.Snapshot.Armed);
        await Docker("start "+component);
        await Wait(Ready);
        for(int i=0;i<30;i++) {
            gateway.FeedSimulation(50,50);await Task.Delay(50);
            if(gateway.Snapshot.Armed || gateway.Snapshot.Left!=0 || RobotMoving())throw new Exception("Automatic restart");
        }
        Console.WriteLine("PASS "+component+" loss/restart remains stopped");
        gateway.FeedSimulation(0,0);await Task.Delay(100);
    }
} finally {cancellation.Cancel();await run;await feeder;}
