import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';

const read = path => readFileSync(new URL(path, import.meta.url));

test('About page ships the exact client wording: 8 parts, 30 rules, 5 recommendations', () => {
  const source = read('../../../docs/client-content/about-tamil.md');
  const bundled = read('../content/about-tamil.md');
  assert.deepEqual(bundled, source);
  const text = source.toString('utf8');
  assert.ok(text.includes('சங்கத்தில் உறுப்பினராக உள்ள அனைத்து உறுப்பினர்களுக்கும் மூன்று ஆண்டுகளுக்கு ஒருமுறை உறுப்பினர் அடையாள அட்டை புதுப்பித்து வழங்கப்பட வேண்டும்.'));
  assert.ok(!text.includes('இரண்டு ஆண்டுகளுக்கு ஒருமுறை'));
  assert.equal((text.match(/^# பகுதி\s*[–-]\s*\d+$/gm) || []).length, 8);
  assert.deepEqual([...text.matchAll(/^### விதி (\d+)/gm)].map(m => Number(m[1])), Array.from({ length: 30 }, (_, i) => i + 1));
  assert.equal((text.split('# முக்கியமான சில ஆலோசனைகள்')[1].match(/^\d+\. /gm) || []).length, 5);
  assert.ok(text.includes('# சங்கத்தின் இறுதி உறுதிமொழி'));
});

test('registration page ships the exact ten rules and concluding pledge', () => {
  const source = read('../../../docs/client-content/registration-terms-tamil.md');
  assert.deepEqual(read('../content/registration-terms-tamil.md'), source);
  const text = source.toString('utf8');
  assert.ok(text.includes('அனைத்து உறுப்பினர்களுக்கும் மூன்று ஆண்டுகளுக்கு ஒருமுறை அடையாள அட்டை புதுப்பித்து வழங்கப்பட வேண்டும்.'));
  assert.ok(!text.includes('இரண்டு ஆண்டுகளுக்கு ஒருமுறை'));
  assert.deepEqual([...text.matchAll(/^### (\d+)\./gm)].map(m => Number(m[1])), Array.from({ length: 10 }, (_, i) => i + 1));
  assert.ok(text.includes('நமது சங்கத்தின் முக்கியக் கொள்கை'));
});

test('the client VERIFIED artwork is unchanged', () => {
  assert.equal(createHash('sha256').update(read('../../public/images/verified-sticker.png')).digest('hex'),
    '16e63058c01704621bdc230508c61b8d973b25ccfa3abcb92de2e957f1b7c30e');
});
