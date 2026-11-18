# Vercel deployment

Project: `sumoos`. Git repository: `JeroenMarkusVermeulen/Sumoos`. Root directory: `website`.

Run `npm run build` with Node.js 22 or newer. No npm dependencies are required. The build outputs only HTML and CSS to `dist/`, so PHP source and documentation are not published.

Edit the original PHP pages and shared includes. The build supports the existing header/footer includes and copyright year expression; it is not a general PHP interpreter. It fails on unsupported PHP. A future server-side contact form requires a separate backend or a deliberate runtime migration. The current Contact page is informational only.

Pushes to the production branch trigger Vercel deployments. Page routes: `/`, `/about`, `/contact`; old `.php` links redirect to their corresponding page.
