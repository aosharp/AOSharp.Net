import { sendToHost } from '../bridge';
import { selectProfiles, useStore } from '../store';

export function ProfileList() {
  const allProfiles = useStore(selectProfiles);
  const activeProfileId = useStore((s) => s.activeProfileId);
  const profiles = allProfiles.filter((p) => p.isActive);

  function handleSelect(id: string) {
    sendToHost({ type: 'selectProfile', profileId: id });
  }

  return (
    <div
      style={{
        width: '23%',
        minWidth: 140,
        borderRight: '1px solid var(--color-border)',
        overflowY: 'auto',
        display: 'flex',
        flexDirection: 'column',
      }}
    >
      {profiles.length === 0 && (
        <p style={{ color: 'var(--color-text-muted)', padding: '8px 12px', fontSize: 13 }}>
          No profiles
        </p>
      )}
      {profiles.map((p) => (
        <button
          key={p.id}
          onClick={() => handleSelect(p.id)}
          style={{
            background: p.id === activeProfileId ? 'var(--color-surface-hover)' : 'transparent',
            border: 'none',
            borderLeft:
              p.id === activeProfileId
                ? '3px solid var(--color-accent)'
                : '3px solid transparent',
            color: p.isInjected ? 'var(--color-green)' : 'var(--color-text)',
            textAlign: 'left',
            padding: '8px 12px',
            cursor: 'pointer',
            fontSize: 13,
            width: '100%',
          }}
          onMouseEnter={(e) => {
            if (p.id !== activeProfileId)
              (e.currentTarget as HTMLButtonElement).style.background =
                'var(--color-surface)';
          }}
          onMouseLeave={(e) => {
            if (p.id !== activeProfileId)
              (e.currentTarget as HTMLButtonElement).style.background = 'transparent';
          }}
        >
          {p.name}
        </button>
      ))}
    </div>
  );
}
