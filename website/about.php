<?php include 'inc/header.php'; ?>

<section class="page-header">
    <h1>About Our Team</h1>
    <p>An international TTO graduation project built by IT students from the Netherlands and Denmark.</p>
</section>

<section class="content-section">
    <div class="about-grid">
        <div class="about-text">
            <h2>Who Are We?</h2>
            <p>We are 4 final-year IT students from the Netherlands and Denmark. As part of our Bilingual Education (TTO) program, we teamed up across borders to design, build, and program a competitive sumobot system from scratch.</p>
            <p>Because our team works remotely from two countries, we rely on structured workflows using GitHub for version control and Teams.</p>
        </div>

        <div class="stats-grid">
            <div class="stat-box">
                <span class="stat-number">4</span>
                <span class="stat-label">IT Developers</span>
            </div>
            <div class="stat-box">
                <span class="stat-number">NL & DK</span>
                <span class="stat-label">International Team</span>
            </div>
            <div class="stat-box">
                <span class="stat-number">TTO</span>
                <span class="stat-label">Bilingual Program</span>
            </div>
            <div class="stat-box">
                <span class="stat-number">2 Bots</span>
                <span class="stat-label">Auto & Manual</span>
            </div>
        </div>
    </div>
</section>

<section class="content-section bg-darker">
    <h2 class="section-title">Meet the Developers</h2>
    <p class="section-intro">Each team member owns a core part of the hardware, software, or communication architecture.</p>

    <div class="team-grid">
        <div class="team-card">
            <div class="team-card-header">
                <div class="member-avatar">OL</div>
                <span class="country-tag">Denmark</span>
            </div>
            <h3>Oliver</h3>
            <span class="member-role">Autonomous Systems</span>
            <p>Programs the autonomous robot, handling opponent detection, search patterns, and combat logic.</p>
            <div class="tech-tags">
                <span>Zumo 2040</span>
                <span>MicroPython</span>
                <span>Sensors</span>
            </div>
        </div>

        <div class="team-card">
            <div class="team-card-header">
                <div class="member-avatar">VL</div>
                <span class="country-tag">Denmark</span>
            </div>
            <h3>Vlad</h3>
            <span class="member-role">Hardware & Controller</span>
            <p>Develops the custom Arduino-based physical controller for driving the manual sumobot.</p>
            <div class="tech-tags">
                <span>Arduino</span>
                <span>C++</span>
                <span>USB Serial</span>
            </div>
        </div>

        <div class="team-card">
            <div class="team-card-header">
                <div class="member-avatar">JR</div>
                <span class="country-tag">Netherlands</span>
            </div>
            <h3>Jeroen</h3>
            <span class="member-role">Backend & Communication</span>
            <p>Builds the .NET application to process controller input and route commands over MQTT to the robot.</p>
            <div class="tech-tags">
                <span>.NET / C#</span>
                <span>MQTT</span>
                <span>Docker</span>
            </div>
        </div>

        <div class="team-card">
            <div class="team-card-header">
                <div class="member-avatar">RM</div>
                <span class="country-tag">Netherlands</span>
            </div>
            <h3>Ramazan</h3>
            <span class="member-role">Web Development & UI/UX</span>
            <p>Designs and develops the Sumoos business website, contact backend, and project documentation.</p>
            <div class="tech-tags">
                <span>PHP</span>
                <span>HTML / CSS</span>
                <span>UI/UX</span>
            </div>
        </div>
    </div>
</section>

<?php include 'inc/footer.php'; ?>