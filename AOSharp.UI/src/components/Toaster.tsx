import { useStore } from '../store';

export function Toaster() {
  const toasts = useStore((s) => s.toasts);
  const dismiss = useStore((s) => s.dismissToast);

  if (toasts.length === 0) return null;

  return (
    <div
      style={{
        position: 'fixed',
        bottom: 16,
        right: 16,
        display: 'flex',
        flexDirection: 'column',
        gap: 8,
        zIndex: 900,
        maxWidth: 360,
      }}
    >
      {toasts.map((t) => (
        <div
          key={t.id}
          onClick={() => dismiss(t.id)}
          style={{
            background: t.level === 'error' ? '#3d1a1a' : 'var(--color-surface)',
            border: `1px solid ${t.level === 'error' ? 'var(--color-red)' : 'var(--color-border)'}`,
            borderRadius: 6,
            padding: '10px 14px',
            cursor: 'pointer',
            boxShadow: '0 4px 12px rgba(0,0,0,0.4)',
          }}
        >
          <div style={{ fontWeight: 600, fontSize: 13, marginBottom: 2 }}>{t.title}</div>
          <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>{t.message}</div>
        </div>
      ))}
    </div>
  );
}
