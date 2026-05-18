import { sendToHost } from '../bridge';
import { selectActiveProfile, selectUiLocked, useStore } from '../store';

export function LoadoutBar() {
  const activeProfile = useStore(selectActiveProfile);
  const loadouts = useStore((s) => s.loadouts);
  const autoInject = useStore((s) => s.autoInject);
  const uiLocked = useStore(selectUiLocked);

  if (!activeProfile) return null;

  const isInjected = activeProfile.isInjected;
  const controlsDisabled = isInjected || uiLocked;
  const activeLoadout = loadouts.find((l) => l.id === activeProfile.loadoutId);

  function handleInjectEject() {
    if (uiLocked) return;
    sendToHost(isInjected ? { type: 'eject' } : { type: 'inject' });
  }

  return (
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 10,
          padding: '6px 12px',
          borderBottom: '1px solid var(--color-border)',
          background: 'var(--color-surface)',
          flexShrink: 0,
          minHeight: 36,
        }}
      >
        <span style={{ fontSize: 12, color: 'var(--color-text-muted)', flexShrink: 0 }}>Loadout</span>
        <select
          value={activeProfile.loadoutId}
          disabled={controlsDisabled}
          onChange={(e) =>
            sendToHost({ type: 'assignLoadout', profileId: activeProfile.id, loadoutId: e.target.value })
          }
          style={controlsDisabled ? selectDisabledStyle : selectStyle}
          title={
            uiLocked
              ? 'Wait for the current operation to finish'
              : isInjected
                ? 'Eject to change loadout'
                : undefined
          }
          aria-disabled={controlsDisabled}
        >
          {loadouts.map((l) => (
            <option key={l.id} value={l.id}>
              {l.name}
            </option>
          ))}
        </select>
        {activeLoadout && (
          <span
            style={{
              fontSize: 11,
              color: 'var(--color-text-muted)',
              flex: 1,
              minWidth: 0,
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            }}
          >
            {activeLoadout.pluginKeys.length} plugin{activeLoadout.pluginKeys.length === 1 ? '' : 's'} in loadout
          </span>
        )}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            flexShrink: 0,
            marginLeft: 'auto',
          }}
        >
          <label
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 6,
              fontSize: 12,
              color: 'var(--color-text-muted)',
              cursor: uiLocked ? 'not-allowed' : 'pointer',
              opacity: uiLocked ? 0.45 : 1,
            }}
            title={
              uiLocked
                ? 'Wait for the current operation to finish'
                : "Inject each character's saved loadout when their game window appears"
            }
          >
            <input
              type="checkbox"
              checked={autoInject}
              disabled={uiLocked}
              onChange={(e) => sendToHost({ type: 'setAutoInject', enabled: e.target.checked })}
              style={{ cursor: uiLocked ? 'not-allowed' : 'pointer' }}
            />
            Auto-inject
          </label>
          <button
            type="button"
            onClick={handleInjectEject}
            disabled={uiLocked}
            title={
              uiLocked
                ? 'Wait for the current operation to finish'
                : isInjected
                  ? `Eject ${activeProfile.name}`
                  : `Inject ${activeProfile.name}'s loadout`
            }
            style={{
              ...injectBtnStyle,
              background: isInjected ? '#3d1a1a' : 'var(--color-surface-hover)',
              borderColor: isInjected ? 'var(--color-red)' : 'var(--color-border)',
              opacity: uiLocked ? 0.45 : 1,
              cursor: uiLocked ? 'not-allowed' : 'pointer',
            }}
          >
            {isInjected ? 'Eject' : 'Inject'}
          </button>
        </div>
      </div>
  );
}

const injectBtnStyle: React.CSSProperties = {
  background: 'var(--color-surface-hover)',
  border: '1px solid var(--color-border)',
  borderRadius: 4,
  color: 'var(--color-text)',
  padding: '4px 10px',
  cursor: 'pointer',
  fontSize: 12,
  minWidth: 64,
};

const selectStyle: React.CSSProperties = {
  background: 'var(--color-bg)',
  border: '1px solid var(--color-border)',
  borderRadius: 4,
  color: 'var(--color-text)',
  padding: '4px 8px',
  fontSize: 12,
  minWidth: 120,
  maxWidth: 180,
  cursor: 'pointer',
};

const selectDisabledStyle: React.CSSProperties = {
  ...selectStyle,
  opacity: 0.45,
  cursor: 'not-allowed',
  color: 'var(--color-text-muted)',
  background: 'var(--color-surface)',
  borderColor: 'var(--color-border)',
  pointerEvents: 'none',
};
