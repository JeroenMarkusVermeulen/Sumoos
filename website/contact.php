<?php include 'inc/header.php'; ?>

<section class="page-header">
    <h1>Contact Us</h1>
    <p>Have any questions about the Sumoos project? Get in touch with us.</p>
</section>

<section class="content-section">
    <div class="text-block">
        
        <!-- Toon de melding als het formulier is ingevuld -->
        <p>Submitting opens a draft in your email app. Review it and press Send there. No message is sent by this website.</p><p>You can also email <a href="mailto:sumbots2026@gmail.com">sumbots2026@gmail.com</a>.</p>

        <!-- actie is leeg ("") zodat hij naar deze zelfde pagina post -->
        <form class="contact-form" action="mailto:sumbots2026@gmail.com" method="GET" id="contact-form">
            <div class="form-group">
                <label for="name">Name</label>
                <input type="text" id="name" name="name" placeholder="Your name" required>
            </div>
            <div class="form-group">
                <label for="email">Email</label>
                <input type="email" id="email" name="email" placeholder="Your email address" required>
            </div>
            <div class="form-group">
                <label for="subject">Subject</label>
                <input type="text" id="subject" name="subject" placeholder="What is this about?" required>
            </div>
            <div class="form-group">
                <label for="message">Message</label>
                <textarea id="message" name="message" rows="5" placeholder="Write your message here..." required></textarea>
            </div>
            <button type="submit" class="btn">Open Email Draft</button>
        </form>
    </div>
</section>

<script src="/js/contact.js" defer></script>
<?php include 'inc/footer.php'; ?>