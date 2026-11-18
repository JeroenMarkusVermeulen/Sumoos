import { readFile, writeFile, mkdir, rm, cp } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(root, 'dist');
await rm(out, { recursive: true, force: true });
await mkdir(out, { recursive: true });

async function render(name, chain = []) {
  if (chain.includes(name)) throw new Error('Circular PHP include: ' + name);
  let source = await readFile(path.join(root, name), 'utf8');
  const includes = [...source.matchAll(/<\?php\s+include\s+['"]([^'"]+)['"];\s*\?>/g)];
  for (const match of includes) {
    if (!['inc/header.php', 'inc/footer.php'].includes(match[1])) {
      throw new Error('Unsupported include: ' + match[1]);
    }
    source = source.replace(match[0], await render(match[1], [...chain, name]));
  }
  source = source.replace(/<\?php\s+echo\s+date\(["']Y["']\);\s*\?>/g, String(new Date().getUTCFullYear()));
  if (source.includes('<?')) throw new Error('Unsupported PHP in ' + name + ': add proper server-side support before publishing.');
  return source.replace(/href=["']index\.php["']/g, 'href="/"')
    .replace(/href=["'](about|contact)\.php["']/g, 'href="/$1"');
}

for (const page of ['index', 'about', 'contact']) {
  await writeFile(path.join(out, page + '.html'), await render(page + '.php'));
}
await cp(path.join(root, 'css'), path.join(out, 'css'), { recursive: true });
console.log('Built Home, About and Contact pages into dist/.');
