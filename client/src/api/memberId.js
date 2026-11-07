// The server owns formatting/collision decisions. Keep the stored ID for lookups.
export const memberIdDisplay = (member) => member?.displayMemberId ?? member?.memberId ?? '';
export const memberIdFilePart = (memberId) => String(memberId || 'MEMBER').replace(/[^a-z0-9-]/gi, '_');
