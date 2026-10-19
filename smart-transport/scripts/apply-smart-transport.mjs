// Patches a local, audited copy of the Cloudflare production source.
// Never deploys, never reads production KV, and fails if its anchors changed.
import { readFile, writeFile } from 'node:fs/promises';
const [original, candidate] = process.argv.slice(2);
if (!original || !candidate || original === candidate) {
  console.error('Usage: node scripts/apply-smart-transport.mjs ORIGINAL_WORKER.js CANDIDATE_WORKER.js');
  process.exit(2);
}
let source = await readFile(original,'utf8');
let addon = await readFile(new URL('../src/smart-transport.mjs',import.meta.url),'utf8');
addon = addon.replace('export async function smartTransportDecision','async function smartTransportDecision')
             .replace('export async function smartTransportMaybeRedirect','async function smartTransportMaybeRedirect');
const anchors = [
  ['async function servePkg(request, route) {','async function servePkg(request, route, env) {\n  const directResponse = await smartTransportMaybeRedirect(request, route, env);\n  if (directResponse) return directResponse;'],
  ['try { outcome = await servePkg(request, route); }','try { outcome = await servePkg(request, route, env); }']
];
for(const [oldText,newText] of anchors){
 const parts=source.split(oldText);
 if(parts.length!==2) throw Error(`Unique anchor missing/changed: ${oldText}`);
 source=parts.join(newText);
}
if(source.includes('function smartTransportDecision('))throw Error('Already patched');
await writeFile(candidate, addon+'\n'+source);
console.log('Created candidate without modifying baseline:', candidate);
