const KEY = 'wtsRegistrationDraftV1';
const LIFETIME = 3 * 60 * 1000;
const FIELDS = ['dateOfBirth', 'bloodGroup', 'fatherName', 'permanentAddress', 'pincode', 'district', 'hasJoinedWhatsAppGroup', 'consentAccepted'];

export function saveRegistrationDraft(identity, form) {
  const fields = Object.fromEntries(FIELDS.map((key) => [key, form[key]]));
  try { sessionStorage.setItem(KEY, JSON.stringify({ expires: Date.now() + LIFETIME, name: identity.name, phoneNumber: identity.phoneNumber, fields })); } catch { /* Storage can be unavailable on mobile. */ }
}

export function readRegistrationDraft() {
  try {
    const raw = sessionStorage.getItem(KEY);
    if (!raw) return null;
    const draft = JSON.parse(raw);
    if (draft.expires <= Date.now()) { clearRegistrationDraft(); return null; }
    return draft;
  } catch { clearRegistrationDraft(); return null; }
}

export function clearRegistrationDraft() {
  try { sessionStorage.removeItem(KEY); } catch { /* Storage can be unavailable. */ }
}
