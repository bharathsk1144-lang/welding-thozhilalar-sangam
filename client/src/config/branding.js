// Client-maintained organization content. Empty values are intentionally not invented.
export const createBranding = (env = {}) => Object.freeze({
  logo: '/wts-logo-client-transparent.png',
  nameTamil: 'வெல்டிங் தொழிலாளர்கள் சங்கம்',
  nameEnglish: 'Welding Thozhilalar Sangam',
  officeAddress: env.VITE_WTS_OFFICE_ADDRESS?.trim() || '146 C1, Velayudha Nagar, T. Palur Main Road\nJayankondam, Ariyalur District – 621802',
  instagramId: env.VITE_WTS_INSTAGRAM_ID?.trim() || '@welding_thozhilalargal_sangam',
  facebookId: env.VITE_WTS_FACEBOOK_ID?.trim() || 'வெல்டிங் தொழிலாளர்கள் சங்கம்',
  youtubeUrl: env.VITE_WTS_YOUTUBE_URL?.trim() || 'https://www.youtube.com/@WeldingThozhilalargalSangam',
  websiteUrl: env.VITE_WTS_WEBSITE_URL?.trim() || 'https://www.weldingthozhilalarsangam.com',
  phone: env.VITE_WTS_PHONE?.trim() || '7373605560',
  email: env.VITE_WTS_EMAIL_APPROVED === 'true' ? env.VITE_WTS_EMAIL?.trim() || '' : '',
  officeHours: '',
  foundedOn: '',
  history: '',
  founder: '',
  achievements: ''
});

export const branding = createBranding(import.meta.env);

export const officeAddressText = (address = branding.officeAddress) => address?.trim() || 'Office address awaiting confirmation';
export const organizationContactRows = (settings = branding) => [
  { label: 'Instagram', value: settings.instagramId?.trim() || 'To be updated' },
  { label: 'Facebook', value: settings.facebookId?.trim() || 'To be updated' },
  { label: 'YouTube', value: settings.youtubeUrl?.replace(/^https?:\/\/(?:www\.)?/i, '').replace(/\/$/, '') || 'To be updated' },
  { label: 'Website', value: settings.websiteUrl.replace(/^https?:\/\//i, '').replace(/\/$/, '') }
];
