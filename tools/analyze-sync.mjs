// Compares two Kino --synclog CSVs recorded on the same PC (shared QPC clock).
// For every viewer sample, interpolates the host's position at the same instant and reports the gap.
// Usage: node tools/analyze-sync.mjs host.csv viewer.csv [bucketSeconds=5]
import { readFileSync } from "node:fs";

const [hostFile, viewerFile, bucketArg] = process.argv.slice(2);
const bucket = Number(bucketArg ?? 5);

function load(file) {
  const rows = [];
  let perturbAt = null;
  for (const line of readFileSync(file, "utf8").split(/\r?\n/).slice(1)) {
    if (!line) continue;
    if (line.startsWith("#")) {
      perturbAt = rows.at(-1)?.t ?? null;
      continue;
    }
    const [t, pos, drift, rate, zone] = line.split(",");
    rows.push({ t: +t, pos: +pos, drift: +drift, rate: +rate, zone });
  }
  return { rows, perturbAt };
}

const host = load(hostFile).rows.filter((r) => Number.isFinite(r.pos));
const { rows: viewer, perturbAt } = load(viewerFile);

function hostPosAt(t) {
  let lo = 0, hi = host.length - 1;
  if (t < host[0].t || t > host[hi].t) return null;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (host[mid].t <= t) lo = mid; else hi = mid;
  }
  const a = host[lo], b = host[hi];
  if (b.t - a.t > 1000) return null; // gap in host log
  return a.pos + ((b.pos - a.pos) * (t - a.t)) / (b.t - a.t);
}

const start = Math.max(host[0].t, viewer[0].t);
const buckets = new Map();
for (const v of viewer) {
  if (!Number.isFinite(v.pos)) continue;
  const hp = hostPosAt(v.t);
  if (hp === null) continue;
  const k = Math.floor((v.t - start) / 1000 / bucket);
  const b = buckets.get(k) ?? { gaps: [], rates: [], zones: new Set() };
  b.gaps.push(v.pos - hp);
  b.rates.push(v.rate);
  b.zones.add(v.zone);
  buckets.set(k, b);
}

const mean = (a) => a.reduce((s, x) => s + x, 0) / a.length;
console.log(`time(s)  gap viewer−host (s)  [min .. max]        rate range         zones`);
for (const [k, b] of [...buckets.entries()].sort((x, y) => x[0] - y[0])) {
  const t0 = k * bucket;
  const mark = perturbAt !== null && perturbAt - start >= t0 * 1000 && perturbAt - start < (t0 + bucket) * 1000 ? "  ← perturbed" : "";
  console.log(
    `${String(t0).padStart(5)}    ${mean(b.gaps).toFixed(3).padStart(7)}   [${Math.min(...b.gaps).toFixed(3)} .. ${Math.max(...b.gaps).toFixed(3)}]`.padEnd(52) +
      `${Math.min(...b.rates).toFixed(4)}–${Math.max(...b.rates).toFixed(4)}   ${[...b.zones].join(",")}${mark}`,
  );
}
