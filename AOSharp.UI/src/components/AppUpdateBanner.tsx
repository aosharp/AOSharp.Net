import { sendToHost } from '../bridge';
import { useStore, selectUiLocked } from '../store';

const bannerStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  padding: '6px 10px',
  background: 'var(--color-accent-muted, #1a3a5c)',
  borderBottom: '1px solid var(--color-border)',
  color: 'var(--color-text)',
  flexShrink: 0,
};

const actionBtnStyle: React.CSSProperties = {
  background: 'var(--color-surface-hover)',
  border: '1px solid var(--color-border)',
  borderRadius: 4,
  color: 'var(--color-text)',
  padding: '3px 10px',
  cursor: 'pointer',
  fontSize: 11,
};

const linkBtnStyle: React.CSSProperties = {
  ...actionBtnStyle,
  background: 'transparent',
};

const dismissBtnStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  color: 'var(--color-text-muted)',
  cursor: 'pointer',
  fontSize: 16,
  lineHeight: 1,
  padding: '0 4px',
};

export function AppUpdateBanner() {
  const appUpdate = useStore((s) => s.appUpdate);
  const uiLocked = useStore(selectUiLocked);

  if (!appUpdate?.bannerVisible) {
    return null;
  }

  const busy = appUpdate.status === 'Downloading' || appUpdate.status === 'Checking';
  const versionLabel = appUpdate.readyToApply
    ? appUpdate.availableVersion ?? appUpdate.currentVersion
    : appUpdate.availableVersion;

  return (
    <div style={bannerStyle}>
      <span style={{ flex: 1, fontSize: 12 }}>
        {appUpdate.readyToApply ? (
          <>AOSharp v{versionLabel} is ready to install.</>
        ) : appUpdate.status === 'Downloading' ? (
          <>Downloading AOSharp v{versionLabel}… {appUpdate.downloadProgressPercent}%</>
        ) : appUpdate.status === 'Error' ? (
          <>Update check failed: {appUpdate.error ?? 'Unknown error'}</>
        ) : (
          <>AOSharp v{versionLabel} is available.</>
        )}
      </span>

      {appUpdate.releaseNotesUrl && (
        <button
          type="button"
          style={linkBtnStyle}
          disabled={uiLocked}
          onClick={() => sendToHost({ type: 'openUrl', url: appUpdate.releaseNotesUrl! })}
        >
          Release notes
        </button>
      )}

      {!appUpdate.readyToApply && appUpdate.status === 'Available' && (
        <button
          type="button"
          style={actionBtnStyle}
          disabled={uiLocked || busy}
          onClick={() => sendToHost({ type: 'downloadAppUpdate' })}
        >
          Download
        </button>
      )}

      {appUpdate.readyToApply && (
        <button
          type="button"
          style={actionBtnStyle}
          disabled={uiLocked || busy}
          onClick={() => sendToHost({ type: 'applyAppUpdate' })}
        >
          Restart to update
        </button>
      )}

      {appUpdate.status === 'Error' && (
        <button
          type="button"
          style={actionBtnStyle}
          disabled={uiLocked || busy}
          onClick={() => sendToHost({ type: 'checkAppUpdate' })}
        >
          Retry
        </button>
      )}

      {!busy && (
        <button
          type="button"
          style={dismissBtnStyle}
          disabled={uiLocked}
          title="Dismiss"
          aria-label="Dismiss update notice"
          onClick={() => sendToHost({ type: 'dismissAppUpdate' })}
        >
          ×
        </button>
      )}
    </div>
  );
}
