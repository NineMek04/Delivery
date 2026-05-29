
const puppeteer = require('puppeteer');
(async () => {
  const browser = await puppeteer.launch();
  const page = await browser.newPage();
  page.on('console', msg => console.log('PAGE LOG:', msg.text()));
  page.on('pageerror', err => console.log('PAGE ERROR:', err.toString()));
  await page.setViewport({ width: 1920, height: 1080 });
  await page.goto('http://localhost:4200', { waitUntil: 'load' });
  await new Promise(r => setTimeout(r, 3000));
  await browser.close();
})();
