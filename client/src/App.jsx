import { Navigate, Route, Routes } from 'react-router-dom';
import HomePage from './pages/HomePage.jsx';
import AboutPage from './pages/AboutPage.jsx';
import RegistrationPage from './pages/RegistrationPage.jsx';
import AdminLoginPage from './pages/AdminLoginPage.jsx';
import AdminDashboardPage from './pages/AdminDashboardPage.jsx';
import IdCardPage from './pages/IdCardPage.jsx';
import UserLoginPage from './pages/UserLoginPage.jsx';
import MyIdCardPage from './pages/MyIdCardPage.jsx';
import VerifyMemberPage from './pages/VerifyMemberPage.jsx';
import ForgotPasswordPage from './pages/ForgotPasswordPage.jsx';
import UserDetailsPage from './pages/UserDetailsPage.jsx';
import ProtectedRoute from './components/ProtectedRoute.jsx';

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="/about" element={<AboutPage />} />
      <Route path="/register" element={<RegistrationPage />} />
      <Route path="/user-login" element={<UserLoginPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/my-id-card" element={<MyIdCardPage />} />
      <Route path="/verify-member" element={<VerifyMemberPage />} />
      <Route path="/payment/:userId" element={<Navigate to="/register" replace />} />
      <Route path="/id-card/:userId" element={<IdCardPage />} />
      <Route path="/admin/login" element={<AdminLoginPage />} />
      <Route
        path="/admin/dashboard"
        element={
          <ProtectedRoute>
            <AdminDashboardPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/admin/users/:id"
        element={
          <ProtectedRoute>
            <UserDetailsPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/admin/payments"
        element={<Navigate to="/admin/dashboard" replace />}
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
