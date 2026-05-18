interface CompileProgress {
  pluginName: string;
  message: string;
}

interface Props {
  progress: CompileProgress | null;
}

export function CompilingOverlay({ progress }: Props) {
  const name = progress?.pluginName?.trim();
  const detail = progress?.message?.trim();
  const primary = name ? `Compiling ${name}…` : 'Compiling…';

  return (
    <>
      <div
        aria-hidden
        style={{
          position: 'fixed',
          inset: 0,
          zIndex: 9999,
          background: 'rgba(10, 10, 18, 0.45)',
          cursor: 'wait',
        }}
        onClick={(e) => e.stopPropagation()}
        onContextMenu={(e) => e.preventDefault()}
        onMouseDown={(e) => e.preventDefault()}
      />
      <div
        role="status"
        aria-live="polite"
        aria-busy="true"
        aria-label={detail ? `${primary} ${detail}` : primary}
        style={{
          position: 'fixed',
          bottom: 20,
          right: 20,
          zIndex: 10000,
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          padding: '10px 16px',
          background: 'var(--color-surface)',
          border: '1px solid var(--color-border)',
          borderRadius: 8,
          boxShadow: '0 8px 24px rgba(0, 0, 0, 0.35)',
          maxWidth: 'min(92vw, 520px)',
          pointerEvents: 'none',
        }}
      >
        <span className="spinner" style={{ width: 18, height: 18, flexShrink: 0 }} />
        <div style={{ minWidth: 0 }}>
          <div
            style={{
              fontSize: 13,
              fontWeight: 600,
              color: 'var(--color-text)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            }}
          >
            {primary}
          </div>
          {detail && (
            <div
              style={{
                marginTop: 2,
                fontSize: 11,
                color: 'var(--color-text-muted)',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
              }}
            >
              {detail}
            </div>
          )}
        </div>
      </div>
    </>
  );
}
