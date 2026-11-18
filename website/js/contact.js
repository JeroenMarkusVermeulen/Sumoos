document.getElementById('contact-form').addEventListener('submit', event => {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  const subject = encodeURIComponent(String(data.get('subject')));
  const body = encodeURIComponent('Name: ' + data.get('name') + '\nEmail: ' + data.get('email') + '\n\n' + data.get('message'));
  window.location.href = 'mailto:sumbots2026@gmail.com?subject=' + subject + '&body=' + body;
});
