import test from 'node:test';
import assert from 'node:assert/strict';
import { smartTransportDecision, smartTransportMaybeRedirect } from '../src/smart-transport.mjs';

const now = Date.UTC(2026,9,19,18,0,0);
const stamp = new Date(now).toISOString().replace(/[-:]/g,'').replace('.000','');
const host = 'vikingfile.' + 'a'.repeat(32) + '.r2.cloudflarestorage.com';
const url = 'https://' + host + '/game.pkg?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Date=' + stamp + '&X-Amz-Expires=86400&X-Amz-Signature=abc123';
const route = {kind:'viking',url,routeExpiresAt:now+86370*1000};
const env = {SMART_DIRECT_ENABLED:'true',SMART_BGFT_REDIRECT_VERIFIED:'true',SMART_DIRECT_PROVIDERS:'viking'};
const magic = new Uint8Array([0x7f,0x43,0x4e,0x54]);
function goodFetch(total='5368709120') { return async (_u, req) => {
  assert.equal(req.headers.Range,'bytes=0-3');
  assert.equal(req.redirect,'manual');
  return new Response(magic,{status:206,headers:{'Content-Type':'application/octet-stream','Content-Range':'bytes 0-3/'+total,'Content-Length':'4'}});
}; }
const decide = (r=route,e=env,f=goodFetch()) => smartTransportDecision(r,e,{fetch:f,now:()=>now});
test('flags default off, no network',async()=>assert.equal((await smartTransportDecision(route,{}, {fetch:()=>{throw Error('must not fetch')}})).reason,'disabled'));
test('requires BGFT verification',async()=>assert.equal((await decide(route,{SMART_DIRECT_ENABLED:'true'})).reason,'bgft_not_verified'));
test('requires DIRECT enable',async()=>assert.equal((await decide(route,{SMART_BGFT_REDIRECT_VERIFIED:'true'})).reason,'disabled'));
test('valid 5 GiB R2 route',async()=>{const d=await decide();assert.equal(d.mode,'direct');assert.equal(d.totalBytes,'5368709120');});
test('redirect 307 preserves signed URL',async()=>{const r=await smartTransportMaybeRedirect(new Request('https://worker.example/d/test.pkg'),route,env,{fetch:goodFetch(),now:()=>now});assert.equal(r.status,307);assert.equal(r.headers.get('location'),url);assert.equal(r.headers.get('Cache-Control'),'no-store');});
test('HEAD allowed only with two flags',async()=>{const r=await smartTransportMaybeRedirect(new Request('https://worker.example/d/test.pkg',{method:'HEAD'}),route,env,{fetch:goodFetch(),now:()=>now});assert.equal(r.status,307);});
test('POST never redirects',async()=>assert.equal(await smartTransportMaybeRedirect(new Request('https://worker.example/d/test.pkg',{method:'POST'}),route,env,{fetch:goodFetch(),now:()=>now}),null));
test('cookie-dependent route stays in proxy',async()=>assert.equal((await decide({...route,cookie:'secret'})).mode,'proxy'));
test('provider requiring context stays in proxy',async()=>assert.equal((await decide({...route,kind:'akira'})).reason,'provider_not_approved'));
test('no host confusion / SSRF',async()=>assert.equal((await decide({...route,url:url.replace(host,'127.0.0.1')})).reason,'unapproved_host'));
test('rejects HTTP origin',async()=>assert.equal((await decide({...route,url:url.replace('https:','http:')})).reason,'unapproved_host'));
test('rejects nonstandard port',async()=>assert.equal((await decide({...route,url:url.replace(host,host+':8443')})).reason,'unapproved_host'));
test('rejects bad signed date',async()=>assert.equal((await decide({...route,url:url.replace(stamp,'20260230T180000Z')})).reason,'invalid_signed_date'));
test('rejects bad signature algorithm',async()=>assert.equal((await decide({...route,url:url.replace('AWS4-HMAC-SHA256','invalid')})).reason,'missing_signature'));
test('rejects near expiry',async()=>assert.equal((await smartTransportDecision(route,env,{fetch:goodFetch(),now:()=>now+84000*1000})).reason,'insufficient_expiry'));
test('rejects 50 GiB without enough expiry budget',async()=>assert.equal((await decide(route,env,goodFetch('53687091200'))).reason,'insufficient_expiry_for_size'));
test('rejects route expiring too early',async()=>assert.equal((await decide({...route,routeExpiresAt:now+1000*1000})).reason,'route_expires_too_soon'));
test('rejects HTML 206',async()=>assert.equal((await decide(route,env,async()=>new Response('<htm',{status:206,headers:{'Content-Type':'text/html','Content-Range':'bytes 0-3/5000','Content-Length':'4'}}))).reason,'invalid_representation'));
test('rejects gzip mutation',async()=>assert.equal((await decide(route,env,async()=>new Response(magic,{status:206,headers:{'Content-Encoding':'gzip','Content-Range':'bytes 0-3/5000','Content-Length':'4'}}))).reason,'invalid_representation'));
test('rejects origin ignoring Range',async()=>assert.equal((await decide(route,env,async()=>new Response(magic,{status:200}))).reason,'range_not_supported'));
test('rejects origin redirect',async()=>assert.equal((await decide(route,env,async()=>new Response(null,{status:302,headers:{Location:'https://different.example'}}))).mode,'proxy'));
test('rejects incorrect offset',async()=>assert.equal((await decide(route,env,async()=>new Response(magic,{status:206,headers:{'Content-Range':'bytes 1-4/5000','Content-Length':'4'}}))).reason,'incorrect_content_range'));
test('rejects false PKG signature',async()=>assert.equal((await decide(route,env,async()=>new Response(new Uint8Array([0,0,0,0]),{status:206,headers:{'Content-Range':'bytes 0-3/5000','Content-Length':'4'}}))).reason,'invalid_pkg_magic'));
test('rejects extra bytes despite Content-Length=4',async()=>assert.equal((await decide(route,env,async()=>new Response(new Uint8Array([127,67,78,84,1]),{status:206,headers:{'Content-Range':'bytes 0-3/5000','Content-Length':'4'}}))).reason,'oversized_probe_body'));
test('network failure returns PROXY',async()=>assert.equal((await decide(route,env,async()=>{throw new Error('upstream failed')})).reason,'probe_failed'));
test('valid 10 GiB integer-safe size',async()=>assert.equal((await decide(route,env,goodFetch('10737418240'))).totalBytes,'10737418240'));

// v0.2: provider opt-in is mandatory, independent of the global feature switches.
test('direct mode is disabled until a provider is explicitly enabled', async()=>{
  const result = await decide(route, {SMART_DIRECT_ENABLED:'true',SMART_BGFT_REDIRECT_VERIFIED:'true'});
  assert.equal(result.reason, 'provider_not_enabled');
});
test('enabled r2 does not enable viking', async()=>{
  const result = await decide(route, {...env, SMART_DIRECT_PROVIDERS:'r2'});
  assert.equal(result.reason, 'provider_not_enabled');
});
test('allowlist cannot bypass forbidden provider kind', async()=>{
  const result = await decide({...route,kind:'akira'}, {...env, SMART_DIRECT_PROVIDERS:'akira'});
  assert.equal(result.reason, 'provider_not_approved');
});
