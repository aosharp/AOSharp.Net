import type { Plugin } from '../types';
import { sendToHost } from '../bridge';
import { selectActiveProfile, selectPluginsMap, useStore } from '../store';
import { useState } from 'react';
import '../index.css';

interface ContextMenu {
  x: number;
  y: number;
  pluginKey: string;
  plugin: Plugin;
}

export function PluginsGrid() {
  const activeProfile = useStore(selectActiveProfile);
  const pluginsMap = useStore(selectPluginsMap);
  const isLoading = useStore((s) => s.isLoading);
  const isCompiling = useStore((s) => s.isCompiling);
  const compileProgress = useStore((s) => s.compileProgress);
  const plugins = Object.entries(pluginsMap).sort(([, a], [, b]) =>
    a.name.localeCompare(b.name, undefined, { sensitivity: 'base' })
  );
  const [ctx, setCtx] = useState<ContextMenu | null>(null);

  const isEnabled = activeProfile !== null;

  function handleToggle(key: string, enabled: boolean) {
    if (!isEnabled) return;
    sendToHost({ type: 'togglePlugin', key, enabled });
  }

  function handleContextMenu(e: React.MouseEvent, key: string, plugin: Plugin) {
    e.preventDefault();
    setCtx({ x: e.clientX, y: e.clientY, pluginKey: key, plugin });
  }

  function closeCtx() {
    setCtx(null);
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
          opacity: isEnabled ? 1 : 0.45,
          pointerEvents: isEnabled ? 'auto' : 'none',
        }}
      >
        <thead>
          <tr>
            <th style={{ ...thStyle, width: 60 }}>Enabled</th>
            <th style={{ ...thStyle, width: 160 }}>Name</th>
            <th style={{ ...thStyle, width: 80 }}>Version</th>
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
                  disabled={plugin.isLibrary}
                  onChange={(e) => handleToggle(key, e.target.checked)}
                  style={{ cursor: plugin.isLibrary ? 'default' : 'pointer' }}
                />
              </td>
              <td style={tdStyle(plugin)}>{plugin.name}</td>
              <td style={tdStyle(plugin)}>{plugin.version ?? ''}</td>
              <td style={{ ...tdStyle(plugin), textAlign: 'center' }}>
                {plugin.pluginType === 'Repo' ? 'Repo' : 'Disk'}
              </td>
              <td style={{ ...tdStyle(plugin), textAlign: 'center' }}>
                {plugin.isLibrary ? 'Library' : 'Plugin'}
              </td>
              <td style={{ ...tdStyle(plugin), textAlign: 'center' }}>
                {plugin.pluginType === 'Repo' ? (
                  isCompiling && !plugin.isCompiled ? (
                    <div
                      className="spinner"
                      title={compileProgress?.pluginName === plugin.name ? compileProgress.message : 'Queued…'}
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
            minWidth: 120,
            boxShadow: '0 4px 12px rgba(0,0,0,0.4)',
          }}
          onClick={(e) => e.stopPropagation()}
        >
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
          {!ctx.plugin.isDefault && (
            <button
              onClick={() => {
                sendToHost({ type: 'removePlugin', key: ctx.pluginKey });
                closeCtx();
              }}
              style={{ ...menuItemStyle, color: 'var(--color-red)' }}
            >
              Remove
            </button>
          )}
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
