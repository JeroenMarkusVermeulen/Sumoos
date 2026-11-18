<?php include 'inc/header.php'; ?>

<section class="hero-section">
    <div class="hero-content">
        <h1>Sumoos: Our Sumobot Project</h1>
        <p>We are building an autonomous sumobot for our final year IT project. Fast, strong, and smart.</p>
        <div class="hero-buttons">
            <a href="about.php" class="btn">Read more</a>
            <a href="contact.php" class="btn btn-secondary">Contact us</a>
        </div>
    </div>
</section>

<section class="content-section">
    <h2 class="section-title">How it works</h2>
    <div class="grid-container half">
        <div class="card">
            <h3>Autonomous Mode</h3>
            <p>The robot drives itself using sensors. We programmed it to search for the opponent and push them out of the ring automatically without any human help.</p>
        </div>
        <div class="card">
            <h3>Remote Control Mode</h3>
            <p>We can also control the robot manually with a controller using Bluetooth or Wi-Fi, just in case we want to drive it ourselves during testing.</p>
        </div>
    </div>
</section>

<section class="content-section bg-darker">
    <h2 class="section-title">Hardware & Software</h2>
    <div class="grid-container triple">
        <div class="card">
            <h4>Sensors</h4>
            <p>We use ultrasonic sensors to find the enemy, and line sensors on the bottom so the bot doesn't accidentally drive out of the ring.</p>
        </div>
        <div class="card">
            <h4>Motors</h4>
            <p>Strong DC motors with grippy wheels give the bot enough pushing power to win the matches.</p>
        </div>
        <div class="card">
            <h4>The Code</h4>
            <p>Everything is programmed in C++ and PHP. We wrote our own logic for the search and attack movements, and built this website to document it.</p>
        </div>
    </div>
</section>

<?php include 'inc/footer.php'; ?>