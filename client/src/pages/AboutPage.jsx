import AppShell from '../components/AppShell.jsx';
import TamilDocument from '../components/TamilDocument.jsx';
import content from '../content/about-tamil.md?raw';

export default function AboutPage() {
  return <AppShell><section className="about-page"><TamilDocument source={content} tableOfContents /></section></AppShell>;
}
