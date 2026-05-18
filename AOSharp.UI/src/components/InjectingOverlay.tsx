import type { InjectQueueItem } from '../types';
import { CheckIcon, CrossIcon } from './PluginGridIcons';

interface Props {
  queue: InjectQueueItem[];
}

const statusLabel: Record<InjectQueueItem['status'], string> = {
  pending: 'Waiting',
  injecting: 'Injecting',
  succeeded: 'Done',
  failed: 'Failed',
};

export function InjectingOverlay({ queue }: Props) {
  const active = queue.filter((q) => q.status === 'injecting').length;
  const pending = queue.filter((q) => q.status === 'pending').length;
  const summary =
    queue.length === 1
      ? 'Injecting character…'
      : active > 0
        ? `Injecting ${active} character${active === 1 ? '' : 's'}…`
        : pending > 0
          ? `Preparing ${pending} injection${pending === 1 ? '' : 's'}…`
          : 'Injecting characters…';

  return (
    <div
      role="alertdialog"
      aria-modal="true"
      aria-busy="true"
      aria-label={summary}
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 10000,
        background: 'rgba(10, 10, 18, 0.72)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        cursor: 'wait',
      }}
      onClick={(e) => e.stopPropagation()}
      onContextMenu={(e) => e.preventDefault()}
      onMouseDown={(e) => e.preventDefault()}
    >
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: 14,
          padding: '28px 36px',
          background: 'var(--color-surface)',
          border: '1px solid var(--color-border)',
          borderRadius: 8,
          boxShadow: '0 12px 40px rgba(0, 0, 0, 0.45)',
          maxWidth: 'min(90vw, 440px)',
          width: '100%',
        }}
      >
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: 10,
          }}
        >
          <div className="spinner" />
          <div style={{ textAlign: 'center' }}>
            <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--color-text)' }}>
              {summary}
            </div>
            <div
              style={{
                marginTop: 4,
                fontSize: 12,
                color: 'var(--color-text-muted)',
              }}
            >
              Please wait — the UI is locked until injection finishes.
            </div>
          </div>
        </div>

        <ul
          style={{
            margin: 0,
            padding: 0,
            listStyle: 'none',
            display: 'flex',
            flexDirection: 'column',
            gap: 6,
            maxHeight: 240,
            overflowY: 'auto',
          }}
        >
          {queue.map((item) => (
            <li
              key={item.profileId}
              style={{
                display: 'flex',
                alignItems: 'flex-start',
                gap: 10,
                padding: '8px 10px',
                borderRadius: 6,
                background:
                  item.status === 'injecting'
                    ? 'var(--color-surface-hover)'
                    : 'var(--color-bg)',
                border: '1px solid var(--color-border)',
              }}
            >
              <QueueStatusIcon status={item.status} />
              <div style={{ flex: 1, minWidth: 0 }}>
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
                  {item.profileName}
                </div>
                <div
                  style={{
                    fontSize: 11,
                    color:
                      item.status === 'failed'
                        ? 'var(--color-red)'
                        : 'var(--color-text-muted)',
                    marginTop: 2,
                  }}
                >
                  {item.message?.trim() || statusLabel[item.status]}
                </div>
              </div>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

function QueueStatusIcon({ status }: { status: InjectQueueItem['status'] }) {
  if (status === 'pending') {
    return (
      <span
        aria-hidden
        style={{
          width: 16,
          height: 16,
          borderRadius: '50%',
          border: '2px solid var(--color-border)',
          flexShrink: 0,
          marginTop: 2,
        }}
      />
    );
  }
  if (status === 'injecting') {
    return <span className="spinner" style={{ width: 16, height: 16, flexShrink: 0 }} aria-hidden />;
  }
  if (status === 'succeeded') {
    return (
      <span style={{ color: 'var(--color-green)', flexShrink: 0, display: 'inline-flex' }} aria-hidden>
        <CheckIcon size={14} />
      </span>
    );
  }
  return (
    <span style={{ color: 'var(--color-red)', flexShrink: 0, display: 'inline-flex' }} aria-hidden>
      <CrossIcon size={14} />
    </span>
  );
}
