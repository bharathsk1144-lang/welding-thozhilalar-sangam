// Self-contained SVG dimensions and colors survive html2canvas's SVG serialization.
export default function OrganizationContactIcon({ label }) {
  return <svg className="pass-contact-icon" xmlns="http://www.w3.org/2000/svg" width="12" height="12"
    viewBox="0 0 24 24" fill="none" stroke="#6d1325" strokeWidth="1.8" strokeLinecap="round"
    strokeLinejoin="round" role="img" aria-label={label}>
    <title>{label}</title>
    {label === 'Instagram' ? <>
      <rect x="3" y="3" width="18" height="18" rx="5" />
      <circle cx="12" cy="12" r="4" />
      <circle cx="17.5" cy="6.5" r="1" fill="#6d1325" stroke="none" />
    </> : label === 'Facebook' ?
      <path d="M14 22v-8h3l.5-4H14V7.5c0-1 .3-1.5 1.5-1.5H18V2.5A25 25 0 0 0 14.5 2C11 2 9 4.1 9 7.5V10H6v4h3v8Z" fill="#6d1325" stroke="none" /> : label === 'YouTube' ? <>
        <rect x="2" y="5" width="20" height="14" rx="4" />
        <path d="m10 9 6 3-6 3Z" fill="#6d1325" stroke="none" />
      </> : <>
        <circle cx="12" cy="12" r="9" />
        <ellipse cx="12" cy="12" rx="4" ry="9" />
        <path d="M3 12h18" />
      </>}
  </svg>;
}
