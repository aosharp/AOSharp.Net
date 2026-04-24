import { useState } from 'react';
import { sendToHost } from '../bridge';
import { useStore } from '../store';

interface Props {
  onClose: () => void;
}

export function TweaksDialog({ onClose }: Props) {
  const [installDir, setInstallDir] = useState('');
  const requestBrowse = useStore((s) => s.requestBrowse);

  const isValid = installDir.trim().length > 0;

  async function handleBrowse() {
    const path = await requestBrowse('directory');
    if (path) setInstallDir(path);
  }

  function handleEnableLAA() {
    if (!isValid) return;
    sendToHost({ type: 'enableLargeAddressAware', installDir });
    onClose();
  }

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        background: 'rgba(0,0,0,0.6)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 500,
      }}
      onClick={onClose}
    >
      <div
        style={{
          background: 'var(--color-surface)',
          border: '1px solid var(--color-border)',
          borderRadius: 6,
          width: 440,
          padding: '20px',
          boxShadow: '0 8px 32px rgba(0,0,0,0.5)',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <h3 style={{ margin: '0 0 20px', fontSize: 15 }}>Tweaks</h3>

        <div style={{ marginBottom: 20 }}>
          <label style={labelStyle}>AO Install Directory</label>
          <div style={{ display: 'flex', gap: 6 }}>
            <input
              readOnly
              value={installDir}
              placeholder="Select AO install folder..."
              style={inputStyle}
            />
            <button onClick={handleBrowse} style={btnSecondary}>
              Browse...
            </button>
          </div>
        </div>

        <button
          onClick={handleEnableLAA}
          disabled={!isValid}
          style={{
            ...btnSecondary,
            width: '100%',
            padding: '10px',
            marginBottom: 20,
            opacity: isValid ? 1 : 0.4,
            cursor: isValid ? 'pointer' : 'not-allowed',
          }}
        >
          Enable Large Address Aware
        </button>

        <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <button onClick={onClose} style={{ ...btnSecondary, minWidth: 80 }}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}

const inputStyle: React.CSSProperties = {
  flex: 1,
  background: 'var(--color-bg)',
  border: '1px solid var(--color-border)',
  borderRadius: 4,
  color: 'var(--color-text)',
  padding: '6px 8px',
  fontSize: 13,
};

const btnSecondary: React.CSSProperties = {
  background: 'var(--color-surface-hover)',
  border: '1px solid var(--color-border)',
  borderRadius: 4,
  color: 'var(--color-text)',
  padding: '6px 12px',
  cursor: 'pointer',
  fontSize: 13,
};

const labelStyle: React.CSSProperties = {
  display: 'block',
  fontSize: 12,
  color: 'var(--color-text-muted)',
  marginBottom: 6,
};
