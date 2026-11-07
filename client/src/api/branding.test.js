import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { branding, createBranding, officeAddressText, organizationContactRows } from '../config/branding.js';

test('branding uses the exact supplied client logo and matching favicon', () => {
  const logo = readFileSync(new URL(`../../public${branding.logo}`, import.meta.url));
  assert.equal(createHash('sha256').update(logo).digest('hex'), 'edd19e2d92f02bb473436267cf6845e51a3efe07012e2f66ec2c40d9802ec4f5');
  assert.equal(logo.subarray(1, 4).toString(), 'PNG');
  assert.equal(logo[25], 6, 'supplied PNG retains its RGBA color type');
  assert.ok(readFileSync(new URL('../../index.html', import.meta.url), 'utf8').includes(`type="image/png" href="${branding.logo}"`));
});

test('card contact defaults use all four client-confirmed values without inventing profile links', () => {
  assert.deepEqual(organizationContactRows(createBranding()), [
    { label: 'Instagram', value: '@welding_thozhilalargal_sangam' },
    { label: 'Facebook', value: 'வெல்டிங் தொழிலாளர்கள் சங்கம்' },
    { label: 'YouTube', value: 'youtube.com/@WeldingThozhilalargalSangam' },
    { label: 'Website', value: 'www.weldingthozhilalarsangam.com' }
  ]);
});

test('public branding configuration trims values without affecting member identity', () => {
  const configured = createBranding({ VITE_WTS_INSTAGRAM_ID: '  @welding_thozhilalargal_sangam  ',
    VITE_WTS_FACEBOOK_ID: ' வெல்டிங் தொழிலாளர்கள் சங்கம் ',
    VITE_WTS_YOUTUBE_URL: ' https://www.youtube.com/@WeldingThozhilalargalSangam/ ',
    VITE_WTS_WEBSITE_URL: ' https://www.weldingthozhilalarsangam.com/ ',
    VITE_WTS_OFFICE_ADDRESS: ' OFFICE-ADDRESS-TEST-ONLY ' });
  assert.deepEqual(organizationContactRows(configured), organizationContactRows(createBranding()));
  assert.equal(configured.officeAddress, 'OFFICE-ADDRESS-TEST-ONLY');
  assert.equal('organizationRegistrationNumber' in configured, false, 'official ID prefix stays on the server');
  assert.deepEqual(organizationContactRows(createBranding({ VITE_WTS_INSTAGRAM_ID: ' ', VITE_WTS_FACEBOOK_ID: ' ', VITE_WTS_YOUTUBE_URL: ' ', VITE_WTS_WEBSITE_URL: ' ' })),
    organizationContactRows(createBranding()));
});

test('confirmed office details are shared defaults and remain configurable', () => {
  const defaults = createBranding();
  assert.equal(defaults.officeAddress, '146 C1, VELAYUDHA NAGAR,\nT.PALUR MAIN ROAD,\nJAYANKONDAM,\nARIYALUR DISTRICT - 621802');
  assert.equal(defaults.phone, '7373605560');
  assert.equal(defaults.email, 'rksankar73@gmail.com');
  const configured = createBranding({ VITE_WTS_PHONE: ' 9000000000 ', VITE_WTS_EMAIL: ' test@example.com ' });
  assert.equal(configured.phone, '9000000000');
  assert.equal(configured.email, 'test@example.com');
});

test('President home address is absent from public branding even if supplied in the environment', () => {
  const settings = createBranding({
    VITE_WTS_PRESIDENT_HOME_ADDRESS: 'PRIVATE-ADDRESS-TEST-ONLY',
    VITE_WTS_PRESIDENT_HOME_ADDRESS_APPROVED_FOR_PUBLICATION: 'true'
  });
  assert.equal('presidentHomeAddress' in settings, false);
  assert.equal('presidentHomeAddressApprovedForPublication' in settings, false);
});

test('office address uses supplied configuration or an honest pending label', () => {
  assert.equal(officeAddressText('  Confirmed office\nExample district  '), 'Confirmed office\nExample district');
  for (const value of ['', '  ', null]) assert.equal(officeAddressText(value), 'Office address awaiting confirmation');
});
