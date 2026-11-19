<?php include 'inc/header.php'; ?>

<section class="hero-section">
    <div class="hero-content">
        <img src="images/logo.jpg" alt="Sumoos Logo" class="hero-logo">
        <h1>Developing the Future of Combat Robotics</h1>
        <p>Autonomous and manual sumobots built for competitive robot sumo.</p>
        <div class="hero-buttons">
            <a href="about.php" class="btn">Meet the Team</a>
            <a href="contact.php" class="btn btn-secondary">Get in Touch</a>
        </div>
    </div>
</section>

<section class="content-section">
    <h2 class="section-title">The Sport</h2>
    <p class="section-intro">Push the opponent out of the ring to win. Fast, aggressive, and smart.</p>
    
    <div class="grid-container triple">
        <div class="card">
            <h3>The Arena</h3>
            <p>Matches take place in a circular ring (Dohyo) with a black surface and a white border.</p>
        </div>
        <div class="card">
            <h3>Boundary Protection</h3>
            <p>Sensors detect the white line to trigger an automatic reverse and prevent self-elimination.</p>
        </div>
        <div class="card">
            <h3>Victory</h3>
            <p>First robot to touch the floor outside the ring loses the match.</p>
        </div>
    </div>
</section>

<section class="content-section bg-darker">
    <h2 class="section-title">Operational Modes</h2>
    <div class="grid-container half">
        <div class="card">
            <h3>Autonomous Mode</h3>
            <p>Fully independent. Uses proximity sensors to track opponents and make split-second attack decisions.</p>
        </div>
        <div class="card">
            <h3>Manual RC Mode</h3>
            <p>Driven via a custom Arduino controller. Commands are routed through a .NET app and MQTT Wi-Fi to the robot.</p>
        </div>
    </div>
</section>

<section class="content-section">
    <h2 class="section-title">Tech Stack</h2>
    <div class="grid-container triple">
        <div class="card">
            <h4>Hardware</h4>
            <p>Pololu Zumo 2040 chassis with 75:1 high-torque motors and an added ESP32 for Wi-Fi.</p>
        </div>
        <div class="card">
            <h4>Communication</h4>
            <p>Arduino &rarr; USB &rarr; .NET App &rarr; Docker MQTT Broker &rarr; Wi-Fi &rarr; ESP32.</p>
        </div>
        <div class="card">
            <h4>Software</h4>
            <p>MicroPython for Zumo, C++ for ESP32/Arduino, C# for .NET, and PHP/HTML/CSS for this website.</p>
        </div>
    </div>
</section>

<?php include 'inc/footer.php'; ?>