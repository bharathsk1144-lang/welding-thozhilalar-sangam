import React from 'react';

function inline(value) {
  return value.split(/(\*\*[^*]+\*\*)/g).map((part, index) =>
    part.startsWith('**') && part.endsWith('**') ? <strong key={index}>{part.slice(2, -2)}</strong> : part);
}

export function sections(source) {
  return [...source.matchAll(/^# (பகுதி\s*[–-]\s*\d+)$/gm)].map((match, index) => ({
    label: match[1], id: `part-${index + 1}`
  }));
}

export default function TamilDocument({ source, tableOfContents = false }) {
  const parts = sections(source);
  let partIndex = 0;
  const blocks = source.trim().split(/\r?\n\s*\r?\n/);
  return <div className="tamil-document" lang="ta">
    {tableOfContents && <nav className="tamil-document__toc" aria-label="பொருளடக்கம்">
      <strong>பொருளடக்கம்</strong>
      <ol>{parts.map(({ label, id }) => <li key={id}><a href={`#${id}`}>{label}</a></li>)}</ol>
      <a href="#final-pledge">இறுதி உறுதிமொழி</a>
      <a href="#recommendations">முக்கியமான சில ஆலோசனைகள்</a>
    </nav>}
    <div className="tamil-document__body">{blocks.map((block, index) => {
      const lines = block.split(/\r?\n/);
      const heading = block.match(/^(#{1,3}) (.+)$/);
      if (heading && lines.length === 1) {
        const level = heading[1].length;
        let id;
        if (/^பகுதி\s*[–-]\s*\d+$/.test(heading[2])) id = `part-${++partIndex}`;
        else if (heading[2] === 'சங்கத்தின் இறுதி உறுதிமொழி') id = 'final-pledge';
        else if (heading[2] === 'முக்கியமான சில ஆலோசனைகள்') id = 'recommendations';
        return React.createElement(`h${level}`, { key: index, id }, heading[2]);
      }
      if (block === '---') return <hr key={index} />;
      if (lines.every((line) => /^\d+\. /.test(line))) return <ol key={index}>
        {lines.map((line, n) => <li key={n}>{inline(line.replace(/^\d+\. /, ''))}</li>)}
      </ol>;
      return <p key={index}>{lines.map((line, n) => <React.Fragment key={n}>{n > 0 && <br />}{inline(line)}</React.Fragment>)}</p>;
    })}</div>
  </div>;
}
