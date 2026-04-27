import type { Plugin } from '../types';
import { sendToHost } from '../bridge';
import { selectActiveProfile, selectPluginsMap, selectProfiles, useStore } from '../store';
import { useState, useEffect } from 'react';
import '../index.css';

interface ContextMenu {
  x: number;
  y: number;
  pluginKey: string;
  plugin: Plugin;
}

interface PendingUpdate {
  key: string;
  plugin: Plugin;
}

export function PluginsGrid() {
  const activeProfile = useStore(selectActiveProfile);
  const profiles = useStore(selectProfiles);
  const pluginsMap = useStore(selectPluginsMap);
  const isLoading = useStore((s) => s.isLoading);
  const isCompiling = useStore((s) => s.isCompiling);
  const compileProgress = useStore((s) => s.compileProgress);
  const plugins = Object.entries(pluginsMap).sort(([, a], [, b]) =>
    a.name.localeCompare(b.name, undefined, { sensitivity: 'base' })
  );
  const [ctx, setCtx] = useState<ContextMenu | null>(null);
  const [pendingUpdate, setPendingUpdate] = useState<PendingUpdate | null>(null);
  const [trustRepoOnUpdate, setTrustRepoOnUpdate] = useState(false);

  useEffect(() => {
    if (pendingUpdate) setTrustRepoOnUpdate(false);
  }, [pendingUpdate]);

  // Keys of plugins that are enabled on at least one currently-injected profile
  const injectedPluginKeys = new Set(
    profiles.filter((p) => p.isInjected).flatMap((p) => p.enabledPlugins)
  );

  const hasActiveProfile = activeProfile !== null;

  function handleToggle(key: string, enabled: boolean) {
    if (!hasActiveProfile) return;
    sendToHost({ type: 'togglePlugin', key, enabled });
  }

  function handleContextMenu(e: React.MouseEvent, key: string, plugin: Plugin) {
    e.preventDefault();
    setCtx({ x: e.clientX, y: e.clientY, pluginKey: key, plugin });
  }

  function closeCtx() {
    setCtx(null);
  }

  function handleUpdateClick(key: string, plugin: Plugin) {
    closeCtx();
    if (plugin.trustedRepo) {
      sendToHost({ type: 'updatePlugin', key });
    } else {
      setPendingUpdate({ key, plugin });
    }
  }

  const thStyle: React.CSSProperties = {
    padding: '6px 8px',
    textAlign: 'left',
    borderBottom: '1px solid var(--color-border)',
    color: 'var(--color-text-muted)',
    fontSize: 12,
    fontWeight: 600,
    whiteSpace: 'nowrap',
    position: 'sticky',
    top: 0,
    background: 'var(--color-surface)',
  };

  const tdStyle = (plugin: Plugin): React.CSSProperties => ({
    padding: '5px 8px',
    fontSize: 12,
    color: plugin.path?.includes('\\obj\\') ? 'var(--color-red)' : 'var(--color-text)',
    fontStyle: plugin.isDefault ? 'italic' : 'normal',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    maxWidth: 260,
  });

  if (isLoading) {
    return (
      <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <div className="spinner" />
      </div>
    );
  }

  return (
    <div style={{ flex: 1, overflowY: 'auto', position: 'relative' }} onClick={closeCtx}>
      <table
        style={{
          width: '100%',
          borderCollapse: 'collapse',
        }}
      >
        <thead>
          <tr>
            <th style={{ ...thStyle, width: 60 }}>Enabled</th>
            <th style={{ ...thStyle, width: 160 }}>Name</th>
            <th style={{ ...thStyle, width: 110 }}>Version</th>
            <th style={{ ...thStyle, width: 60 }}>Source</th>
            <th style={{ ...thStyle, width: 65 }}>Type</th>
            <th style={{ ...thStyle, width: 65 }}>Compiled</th>
            <th style={{ ...thStyle }}>Path</th>
          </tr>
        </thead>
        <tbody>
          {plugins.map(([key, plugin]) => (
            <tr
              key={key}
              onContextMenu={(e) => handleContextMenu(e, key, plugin)}
              style={{ cursor: 'default' }}
              onMouseEnter={(e) =>
                (e.currentTarget.style.background = 'var(--color-surface-hover)')
              }
              onMouseLeave={(e) =>
                (e.currentTarget.style.background = 'transparent')
              }
            >
              <td style={{ ...tdStyle(plugin), textAlign: 'center' }}>
                <input
                  type="checkbox"
                  checked={plugin.isEnabled}
                  disabled={plugin.isLibrary || !hasActiveProfile}
                  title={!hasActiveProfile ? 'Select a profile to enable/disable plugins' : undefined}
                  onChange={(e) => handleToggle(key, e.target.checked)}
                  style={{ cursor: (plugin.isLibrary || !hasActiveProfile) ? 'default' : 'pointer' }}
                />
              </td>
              <td style={{ ...tdStyle(plugin), display: 'flex', alignItems: 'center', gap: 6, maxWidth: 'unset', width: 160 }}>
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', flex: 1 }}>
                  {plugin.name}
                </span>
                {plugin.pluginType === 'Repo' && plugin.hasUpdate && (
                  <span
                    title="Update available — right-click to update"
                    style={{
                      fontSize: 10,
                      fontWeight: 700,
                      color: '#f0c060',
                      border: '1px solid #a06020',
                      borderRadius: 3,
                      padding: '1px 4px',
                      flexShrink: 0,
                      cursor: 'default',
                    }}
                  >
                    UPDATE
                  </span>
                )}
              </td>
              <td style={tdStyle(plugin)}>
                {plugin.pluginType === 'Repo' ? (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
                    <span style={{ fontFamily: 'monospace', color: 'var(--color-text-muted)' }}>
                      {plugin.localCommit ?? '—'}
                    </span>
                    {plugin.remoteCommit && plugin.remoteCommit !== plugin.localCommit && (
                      <span style={{ fontFamily: 'monospace', color: '#f0c060', fontSize: 11 }}>
                        ↑ {plugin.remoteCommit}
                      </span>
                    )}
                  </div>
                ) : (
                  plugin.version ?? ''
                )}
              </td>
              <td style={{ ...tdStyle(plugin), textAlign: 'center' }}>
                {plugin.pluginType === 'Repo' ? 'Repo' : 'Disk'}
              </td>
              <td style={{ ...tdStyle(plugin), textAlign: 'center' }}>
                {plugin.isLibrary ? 'Library' : 'Plugin'}
              </td>
              <td style={{ ...tdStyle(plugin), textAlign: 'center' }}>
                {plugin.pluginType === 'Repo' ? (
                  isCompiling && compileProgress?.pluginName === plugin.name ? (
                    <div
                      className="spinner"
                      title={compileProgress.message}
                      style={{ width: 12, height: 12, borderWidth: 2, display: 'inline-block' }}
                    />
                  ) : (
                    <span style={{ color: plugin.isCompiled ? 'var(--color-green)' : 'var(--color-red)' }}>
                      {plugin.isCompiled ? '✓' : '✗'}
                    </span>
                  )
                ) : (
                  <span style={{ color: 'var(--color-text-muted)' }}>—</span>
                )}
              </td>
              <td style={{ ...tdStyle(plugin), maxWidth: 'unset' }}>{plugin.path}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {/* Context menu */}
      {ctx && (
        <div
          style={{
            position: 'fixed',
            top: ctx.y,
            left: ctx.x,
            background: 'var(--color-surface)',
            border: '1px solid var(--color-border)',
            borderRadius: 4,
            zIndex: 1000,
            minWidth: 160,
            boxShadow: '0 4px 12px rgba(0,0,0,0.4)',
          }}
          onClick={(e) => e.stopPropagation()}
        >
          {ctx.plugin.pluginType === 'Repo' && ctx.plugin.hasUpdate && (() => {
            const blocked = injectedPluginKeys.has(ctx.pluginKey);
            return (
              <button
                onClick={() => !blocked && handleUpdateClick(ctx.pluginKey, ctx.plugin)}
                title={blocked ? 'Eject before updating' : undefined}
                style={{
                  ...menuItemStyle,
                  color: blocked ? 'var(--color-text-muted)' : '#f0c060',
                  cursor: blocked ? 'not-allowed' : 'pointer',
                  opacity: blocked ? 0.5 : 1,
                }}
              >
                ↑ Update Available{blocked ? ' 🔒' : ''}
              </button>
            );
          })()}
          {ctx.plugin.pluginType === 'Repo' && (
            <button
              onClick={() => {
                sendToHost({ type: 'compilePlugin', key: ctx.pluginKey });
                closeCtx();
              }}
              style={menuItemStyle}
            >
              Compile
            </button>
          )}
          {ctx.plugin.pluginType === 'Repo' && ctx.plugin.repoUrl && (
            <button
              onClick={() => {
                const url = ctx.plugin.repoUrl;
                if (url) {
                  sendToHost({ type: 'openUrl', url });
                  closeCtx();
                }
              }}
              style={menuItemStyle}
            >
              More Info
            </button>
          )}
          {!ctx.plugin.isDefault && (() => {
            const blocked = injectedPluginKeys.has(ctx.pluginKey);
            return (
              <>
                <div style={{ height: 1, background: 'var(--color-border)', margin: '3px 0' }} />
                <button
                  onClick={() => {
                    if (blocked) return;
                    sendToHost({ type: 'removePlugin', key: ctx.pluginKey });
                    closeCtx();
                  }}
                  title={blocked ? 'Eject before removing' : undefined}
                  style={{
                    ...menuItemStyle,
                    color: blocked ? 'var(--color-text-muted)' : 'var(--color-red)',
                    cursor: blocked ? 'not-allowed' : 'pointer',
                    opacity: blocked ? 0.5 : 1,
                  }}
                >
                  Remove{blocked ? ' 🔒' : ''}
                </button>
              </>
            );
          })()}
        </div>
      )}

      {/* Update confirmation modal for untrusted repos */}
      {pendingUpdate && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(0,0,0,0.65)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 1100,
          }}
          onClick={() => setPendingUpdate(null)}
        >
          <div
            style={{
              background: 'var(--color-surface)',
              border: '1px solid var(--color-border)',
              borderRadius: 6,
              padding: '20px 24px',
              maxWidth: 440,
              width: '90%',
              boxShadow: '0 8px 32px rgba(0,0,0,0.5)',
            }}
            onClick={(e) => e.stopPropagation()}
          >
            <h4 style={{ margin: '0 0 12px', fontSize: 14, color: '#f0c060' }}>
              ⚠ Confirm Update: {pendingUpdate.plugin.name}
            </h4>
            <p style={{ margin: '0 0 8px', fontSize: 13, color: 'var(--color-text-muted)', wordBreak: 'break-all' }}>
              {pendingUpdate.plugin.repoUrl}
            </p>
            <p style={{ margin: '0 0 12px', fontSize: 13, lineHeight: 1.5 }}>
              Pulling an update will download and compile new code from this repository.
              Malicious updates can compromise your system.
              Only proceed if you have reviewed the incoming changes and confirmed they are safe.
            </p>
            <label
              style={{
                display: 'flex',
                alignItems: 'flex-start',
                gap: 8,
                margin: '0 0 16px',
                fontSize: 12,
                color: 'var(--color-text)',
                cursor: 'pointer',
                lineHeight: 1.4,
              }}
            >
              <input
                type="checkbox"
                checked={trustRepoOnUpdate}
                onChange={(e) => setTrustRepoOnUpdate(e.target.checked)}
                style={{ marginTop: 2, cursor: 'pointer', flexShrink: 0 }}
              />
              <span>
                Trust this repository for future updates
                <span style={{ display: 'block', color: 'var(--color-text-muted)', fontSize: 11, marginTop: 2 }}>
                  Skips this confirmation the next time an update is available.
                </span>
              </span>
            </label>
            <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
              <button
                onClick={() => {
                  sendToHost({ type: 'updatePlugin', key: pendingUpdate.key, trustRepo: trustRepoOnUpdate });
                  setPendingUpdate(null);
                }}
                style={{
                  background: '#3d2a0a',
                  border: '1px solid #a06020',
                  borderRadius: 4,
                  color: '#f0c060',
                  padding: '6px 14px',
                  cursor: 'pointer',
                  fontSize: 13,
                }}
              >
                I have verified — Update
              </button>
              <button
                onClick={() => setPendingUpdate(null)}
                style={{
                  background: 'var(--color-surface-hover)',
                  border: '1px solid var(--color-border)',
                  borderRadius: 4,
                  color: 'var(--color-text)',
                  padding: '6px 14px',
                  cursor: 'pointer',
                  fontSize: 13,
                }}
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

const menuItemStyle: React.CSSProperties = {
  display: 'block',
  width: '100%',
  background: 'none',
  border: 'none',
  color: 'var(--color-text)',
  textAlign: 'left',
  padding: '7px 14px',
  cursor: 'pointer',
  fontSize: 13,
};
