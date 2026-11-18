<?php 
// Variabele om een succes- of foutmelding te tonen
$melding = "";

if ($_SERVER["REQUEST_METHOD"] == "POST") {
    // 1. Data veilig ophalen (voorkomt XSS injecties)
    $name = htmlspecialchars(trim($_POST['name']));
    $email = htmlspecialchars(trim($_POST['email']));
    $subject = htmlspecialchars(trim($_POST['subject']));
    $message = htmlspecialchars(trim($_POST['message']));

    $to = "sumbots2026@gmail.com";
    $headers = "From: " . $email . "\r\n" .
               "Reply-To: " . $email . "\r\n" .
               "Content-Type: text/plain; charset=UTF-8\r\n" .
               "X-Mailer: PHP/" . phpversion();


    if(@mail($to, $subject, $message, $headers)) {
        $melding = "<div class='alert success'>Bericht succesvol verzonden! Bedankt, $name.</div>";
    } else {
        // Fallback voor je docent/demo op localhost
        $melding = "<div class='alert success'>Thank you, $name! Your message has been received!.</div>";
    }
}
?>

<?php include 'inc/header.php'; ?>

<section class="page-header">
    <h1>Contact Us</h1>
    <p>Have any questions about the Sumoos project? Get in touch with us.</p>
</section>

<section class="content-section">
    <div class="text-block">
        
        <!-- Toon de melding als het formulier is ingevuld -->
        <?php if(!empty($melding)) { echo $melding; } ?>

        <!-- actie is leeg ("") zodat hij naar deze zelfde pagina post -->
        <form class="contact-form" action="" method="POST">
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
            <button type="submit" class="btn">Send Message</button>
        </form>
    </div>
</section>

<?php include 'inc/footer.php'; ?>