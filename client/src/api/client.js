import { resolveApiBase } from './baseUrl.js';
import { notifyStatisticsChanged } from './statistics.js';
const API_BASE_URL = resolveApiBase(import.meta.env.VITE_API_BASE_URL, window.location);
export const FILE_BASE_URL = API_BASE_URL.replace(/\/api\/?$/, '');

export function fileUrl(path) {
  if (!path) return '';
  if (/^https?:\/\//i.test(path)) return path;
  return `${FILE_BASE_URL}${path.startsWith('/') ? path : `/${path}`}`;
}

function authHeaders() {
  const token = localStorage.getItem('adminToken');
  return token ? { Authorization: `Bearer ${token}` } : {};
}

export function memberHeaders(id, adminOnly = false) {
  let session;
  try { session = JSON.parse(localStorage.getItem('wtsUser') || 'null'); } catch { session = null; }
  const member = sessionStorage.getItem('wtsUserToken');
  const admin = localStorage.getItem('adminToken');
  const token = adminOnly ? admin : session?.userId === id ? member || admin : admin || member;
  return token ? { Authorization: `Bearer ${token}` } : {};
}

export async function privateImage(path, ownerId, adminOnly, signal) {
  // Never send session tokens to arbitrary URLs supplied through stored data.
  if (!/^\/uploads\/[^/\\?#:]+$/.test(path || '')) throw new Error('Image unavailable');
  const response = await fetch(fileUrl(path), { headers: memberHeaders(ownerId, adminOnly), signal, cache: 'no-store' });
  if (!response.ok || !response.headers.get('content-type')?.startsWith('image/')) throw new Error('Image unavailable');
  return response.blob();
}

export async function privateOfficerSignature(officer, ownerId, signal) {
  if (!ownerId || !['president', 'secretary', 'treasurer'].includes(officer)) throw new Error('Signature unavailable');
  const response = await fetch(`${API_BASE_URL}/users/${encodeURIComponent(ownerId)}/officer-signatures/${officer}`, {
    headers: memberHeaders(ownerId), signal, cache: 'no-store'
  });
  if (!response.ok || response.headers.get('content-type')?.split(';')[0] !== 'image/png') throw new Error('Signature unavailable');
  return response.blob();
}

export async function downloadPrivateDocument(path) {
  if (!/^\/uploads\/[^/\\?#:]+$/.test(path || '')) throw new Error('Document unavailable');
  const response = await fetch(fileUrl(path), { headers: memberHeaders(null, true), cache: 'no-store' });
  if (!response.ok) throw new Error('Document unavailable. Please login as admin and try again.');
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a');
  link.href = url; link.download = path.split('/').pop();
  document.body.appendChild(link); link.click(); link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

async function request(path, options = {}) {
  const url = `${API_BASE_URL}${path}`;
  let response;

  try {
    response = await fetch(url, {
      ...options,
      headers: {
        ...(options.body instanceof FormData ? {} : { 'Content-Type': 'application/json' }),
        ...authHeaders(),
        ...options.headers
      }
    });
  } catch (err) {
    throw new Error(`Server is not running. Please start backend and try again. (${url})`);
  }

  const contentType = response.headers.get('content-type') || '';
  const data = contentType.includes('application/json') ? await response.json() : null;

  if (!response.ok) {
    const error = new Error(data?.message || `API request failed for ${url}. Please try again.`);
    error.status = response.status;
    error.code = data?.code;
    throw error;
  }

  return data;
}

async function download(path, fallbackFileName) {
  const url = `${API_BASE_URL}${path}`;
  let response;

  try {
    response = await fetch(url, {
      headers: {
        ...authHeaders()
      }
    });
  } catch (err) {
    throw new Error(`Server is not running. Please start backend and try again. (${url})`);
  }

  if (!response.ok) {
    let message = `API request failed for ${url}. Please try again.`;
    try {
      const data = await response.json();
      message = data?.message || message;
    } catch {
      // Non-JSON export errors fall back to the generic message.
    }
    throw new Error(message);
  }

  const blob = await response.blob();
  const disposition = response.headers.get('content-disposition') || '';
  const match = disposition.match(/filename="?([^"]+)"?/i);
  return {
    blob,
    fileName: match?.[1] || fallbackFileName
  };
}

export const api = {
  getDistricts: () => request('/districts'),
  getRegistrationOptions: () => request('/auth/registration-options'),
  sendOtp: (payload) => request('/auth/send-otp', { method: 'POST', body: JSON.stringify(payload) }),
  verifyOtp: (payload) => request('/auth/verify-otp', { method: 'POST', body: JSON.stringify(payload) }),
  registerUser: async (formData) => {
    const result = await request('/users/register', { method: 'POST', body: formData });
    notifyStatisticsChanged();
    return result;
  },
  userLogin: (payload) => request('/users/login', { method: 'POST', body: JSON.stringify(payload) }),
  forgotPasswordSendOtp: (payload) => request('/users/forgot-password/send-otp', { method: 'POST', body: JSON.stringify(payload) }),
  forgotPasswordReset: (payload) => request('/users/forgot-password/reset', { method: 'POST', body: JSON.stringify(payload) }),
  verifyMember: (memberId) => request(`/users/verify/${encodeURIComponent(memberId)}`),
  submitPayment: (formData) => request('/payments/submit', { method: 'POST', body: formData }),
  adminLogin: (payload) => request('/admin/login', { method: 'POST', body: JSON.stringify(payload) }),
  dashboard: () => request('/dashboard/summary'),
  adminUsers: (district) =>
    district ? request(`/users/by-district/${encodeURIComponent(district)}`) : request('/users'),
  adminUserDetails: (id) => request(`/users/${id}`),
  adminUpdateUser: (id, details) => request(`/admin/users/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(details) }),
  setMemberApproval: (id, approved) => request(`/admin/users/${id}/approval`, { method: 'PUT', body: JSON.stringify({ approved }) }),
  pendingPayments: () => request('/payments/pending'),
  exportUsers: () => download('/admin/users/export', `WTS-Members-Export-${new Date().toISOString().slice(0, 10).replaceAll('-', '')}.csv`),
  verifyPayment: (id) => request(`/payments/${id}/verify`, { method: 'PUT' }),
  rejectPayment: (id) => request(`/payments/${id}/reject`, { method: 'PUT' }),
  idCard: (id) => {
    return request(`/users/${id}/id-card`, { headers: memberHeaders(id) });
  },
  createDistrict: (payload) => request('/districts', { method: 'POST', body: JSON.stringify(payload) }),
  updateDistrict: (id, payload) => request(`/districts/${id}`, { method: 'PUT', body: JSON.stringify(payload) })
};
