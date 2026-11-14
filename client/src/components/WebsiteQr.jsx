import { useEffect, useState } from 'react';
import QRCode from 'qrcode';

const WEBSITE = 'https://www.weldingthozhilalarsangam.com/';

export default function WebsiteQr() {
  const [src, setSrc] = useState('');
  useEffect(() => {
    QRCode.toDataURL(WEBSITE, { errorCorrectionLevel: 'M', margin: 4, width: 240, color: { dark: '#171717', light: '#ffffff' } }).then(setSrc);
  }, []);
  return <div className="pass-website-qr">{src && <img src={src} alt="QR code to visit the WTS website" width="84" height="84" />}<small>Scan to Visit Website</small></div>;
}
