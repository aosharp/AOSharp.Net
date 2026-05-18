import type { Plugin } from '../types';
import { sendToHost } from '../bridge';
import { selectPluginsMap, selectProfiles, selectUiLocked, useStore } from '../store';
import { getPluginSection, PLUGIN_SECTIONS, type PluginSection } from '../pluginSections';
import { Fragment, useState, useEffect, useMemo } from 'react';
import '../index.css';
import { LockIcon } from './LockIcon';
import {
  ArrowUpIcon,
  CheckIcon,
  CrossIcon,
  EmptyCellIcon,
  EmDashIcon,
  LoadoutDotIcon,
  WarningIcon,
} from './PluginGridIcons';

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

const COLUMN_COUNT = 7;
const DIMMED_ROW_OPACITY = 0.42;
const DIMMED_ROW_HOVER_OPACITY = 0.62;

export function PluginsGrid() {
  const profiles = useStore(selectProfiles);
  const loadouts = useStore((s) => s.loadouts);
  const pluginsMap = useStore(selectPluginsMap);
  const isLoading = useStore((s) => s.isLoading);
  const uiLocked = useStore(selectUiLocked);
  const isCompiling = useStore((s) => s.isCompiling);
  const compileProgress = useStore((s) => s.compileProgress);
  const activeProfileId = useStore((s) => s.activeProfileId);
  const pluginsBySection = useMemo(() => {
    const grouped = new Map<PluginSection, [string, Plugin][]>();
    for (const section of PLUGIN_SECTIONS) grouped.set(section, []);
    for (const entry of Object.entries(pluginsMap)) {
      const section = getPluginSection(entry[1]);
      grouped.get(section)!.push(entry);
    }
    for (const [, entries] of grouped) {
      entries.sort(([, a], [, b]) =>
        a.name.localeCompare(b.name, undefined, { sensitivity: 'base' })
      );
    }
    return grouped;
  }, [pluginsMap]);
  const [ctx, setCtx] = useState<ContextMenu | null>(null);
  const [pendingUpdate, setPendingUpdate] = useState<PendingUpdate | null>(null);
  const [trustRepoOnUpdate, setTrustRepoOnUpdate] = useState(false);

  useEffect(() => {
    if (pendingUpdate) setTrustRepoOnUpdate(false);
  }, [pendingUpdate]);

  useEffect(() => {
    if (uiLocked) {
      setCtx(null);
      setPendingUpdate(null);
    }
  }, [uiLocked]);

  const injectedPluginKeys = new Set(
    profiles
      .filter((p) => p.isInjected)
      .flatMap((p) => loadouts.find((l) => l.id === p.loadoutId)?.pluginKeys ?? [])
  );

  function handleContextMenu(e: React.MouseEvent, key: string, plugin: Plugin) {
    if (uiLocked) return;
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
    position: 'sticky',
    top: 0,
    background: 'var(--color-surface)',
    zIndex: 1,
  };

  const fitColStyle: React.CSSProperties = {
    width: 0,
    whiteSpace: 'nowrap',
  };

  const tdBase = (plugin: Plugin): React.CSSProperties => ({
    padding: '5px 8px',
    fontSize: 12,
    color: plugin.path?.includes('\\obj\\') ? 'var(--color-red)' : 'var(--color-text)',
    fontStyle: plugin.isDefault || plugin.isManifestDependency ? 'italic' : 'normal',
  });

  const tdStyle = (plugin: Plugin): React.CSSProperties => ({
    ...tdBase(plugin),
    ...fitColStyle,
  });

  const isRowDimmed = (plugin: Plugin) =>
    activeProfileId != null && !plugin.isEnabled;

  const sectionHeaderStyle: React.CSSProperties = {
    padding: '8px 8px 4px',
    fontSize: 11,
    fontWeight: 700,
    letterSpacing: '0.06em',
    textTransform: 'uppercase',
    color: 'var(--color-text-muted)',
    background: 'var(--color-surface)',
    borderBottom: '1px solid var(--color-border)',
  };

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
            <th style={{ ...thStyle, ...fitColStyle }}>In loadout</th>
            <th style={{ ...thStyle, ...fitColStyle }}>Name</th>
            <th style={{ ...thStyle, ...fitColStyle }}>Commit</th>
            <th style={{ ...thStyle, ...fitColStyle }}>Source</th>
            <th style={{ ...thStyle, ...fitColStyle }}>Compiled</th>
            <th style={{ ...thStyle, ...fitColStyle }}>Author</th>
            <th style={thStyle}>Description</th>
          </tr>
        </thead>
        <tbody>
          {PLUGIN_SECTIONS.map((section) => {
            const plugins = pluginsBySection.get(section)!;
            if (plugins.length === 0) return null;
            return (
              <Fragment key={section}>
                <tr>
                  <td colSpan={COLUMN_COUNT} style={sectionHeaderStyle}>
                    {section}
                  </td>
                </tr>
                {plugins.map(([key, plugin]) => {
                  const dimmed = isRowDimmed(plugin);
                  return (
                  <tr
                    key={key}
                    onContextMenu={(e) => handleContextMenu(e, key, plugin)}
                    style={{
                      cursor: 'default',
                      opacity: dimmed ? DIMMED_ROW_OPACITY : 1,
                      transition: 'opacity 0.12s ease, background 0.12s ease',
                    }}
                    onMouseEnter={(e) => {
                      e.currentTarget.style.background = 'var(--color-surface-hover)';
                      if (dimmed) e.currentTarget.style.opacity = String(DIMMED_ROW_HOVER_OPACITY);
                    }}
                    onMouseLeave={(e) => {
                      e.currentTarget.style.background = 'transparent';
                      e.currentTarget.style.opacity = dimmed ? String(DIMMED_ROW_OPACITY) : '1';
                    }}
                  >
                    <td style={{ ...tdStyle(plugin), textAlign: 'center' }}>
                      {!plugin.isLibrary && plugin.isEnabled && (
                        <span
                          title="In active character loadout"
                          style={{
                            color: 'var(--color-accent)',
                            display: 'inline-flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                          }}
                        >
                          <LoadoutDotIcon size={10} />
                        </span>
                      )}
                    </td>
                    <td style={tdStyle(plugin)}>
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, whiteSpace: 'nowrap' }}>
                        {plugin.name}
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
                      </span>
                    </td>
                    <td style={tdStyle(plugin)}>
                      {plugin.pluginType === 'Repo' ? (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
                          <span style={{ fontFamily: 'monospace', color: 'var(--color-text-muted)' }}>
                            {plugin.localCommit ?? <EmptyCellIcon />}
                          </span>
                          {plugin.remoteCommit && plugin.remoteCommit !== plugin.localCommit && (
                            <span
                              style={{
                                fontFamily: 'monospace',
                                color: '#f0c060',
                                fontSize: 11,
                                display: 'inline-flex',
                                alignItems: 'center',
                                gap: 4,
                              }}
                            >
                              <ArrowUpIcon size={11} />
                              {plugin.remoteCommit}
                            </span>
                          )}
                        </div>
                      ) : (
                        <EmptyCellIcon />
                      )}
                    </td>
                    <td style={{ ...tdStyle(plugin), textAlign: 'center' }}>
                      {plugin.pluginType === 'Repo' ? 'Repo' : 'Disk'}
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
                          <span
                            style={{
                              color: plugin.isCompiled ? 'var(--color-green)' : 'var(--color-red)',
                              display: 'inline-flex',
                              alignItems: 'center',
                              justifyContent: 'center',
                            }}
                          >
                            {plugin.isCompiled ? <CheckIcon size={14} /> : <CrossIcon size={14} />}
                          </span>
                        )
                      ) : (
                        <EmptyCellIcon />
                      )}
                    </td>
                    <td style={tdStyle(plugin)} title={(plugin.author ?? '').trim() || undefined}>
                      {(plugin.author ?? '').trim() || <EmptyCellIcon />}
                    </td>
                    <td
                      style={{
                        ...tdBase(plugin),
                        whiteSpace: 'normal',
                        wordBreak: 'break-word',
                        lineHeight: 1.35,
                      }}
                      title={(plugin.description ?? '').trim() || undefined}
                    >
                      {(plugin.description ?? '').trim() || <EmptyCellIcon />}
                    </td>
                  </tr>
                  );
                })}
              </Fragment>
            );
          })}
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
            minWidth: 160,
            boxShadow: '0 4px 12px rgba(0,0,0,0.4)',
          }}
          onClick={(e) => e.stopPropagation()}
        >
          {ctx.plugin.pluginType === 'Repo' && ctx.plugin.hasUpdate && (() => {
            const blocked = injectedPluginKeys.has(ctx.pluginKey);
            const lockTitle = blocked ? 'Eject before updating' : undefined;
            return (
              <button
                onClick={() => !blocked && handleUpdateClick(ctx.pluginKey, ctx.plugin)}
                title={lockTitle}
                style={{
                  ...menuItemStyle,
                  color: blocked ? 'var(--color-text-muted)' : '#f0c060',
                  cursor: blocked ? 'not-allowed' : 'pointer',
                  opacity: blocked ? 0.5 : 1,
                }}
              >
                <span style={{ flex: 1, display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                  <ArrowUpIcon size={12} />
                  Update Available
                </span>
                {blocked && <MenuLock title={lockTitle} />}
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
            const injected = injectedPluginKeys.has(ctx.pluginKey);
            const blocked = injected || !ctx.plugin.canRemove;
            const removeTitle = injected
              ? 'Eject before removing'
              : ctx.plugin.removeBlockedReason ?? undefined;
            return (
              <>
                <div style={{ height: 1, background: 'var(--color-border)', margin: '3px 0' }} />
                <button
                  onClick={() => {
                    if (blocked) return;
                    sendToHost({ type: 'removePlugin', key: ctx.pluginKey });
                    closeCtx();
                  }}
                  title={removeTitle}
                  style={{
                    ...menuItemStyle,
                    color: blocked ? 'var(--color-text-muted)' : 'var(--color-red)',
                    cursor: blocked ? 'not-allowed' : 'pointer',
                    opacity: blocked ? 0.5 : 1,
                  }}
                >
                  <span style={{ flex: 1 }}>Remove</span>
                  {blocked && <MenuLock title={removeTitle} />}
                </button>
              </>
            );
          })()}
        </div>
      )}

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
            <h4
              style={{
                margin: '0 0 12px',
                fontSize: 14,
                color: '#f0c060',
                display: 'flex',
                alignItems: 'center',
                gap: 8,
              }}
            >
              <WarningIcon size={16} />
              Confirm Update: {pendingUpdate.plugin.name}
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
                  display: 'inline-flex',
                  alignItems: 'center',
                }}
              >
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                  I have verified
                  <EmDashIcon size={12} />
                  Update
                </span>
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
  display: 'flex',
  alignItems: 'center',
  gap: 6,
  width: '100%',
  background: 'none',
  border: 'none',
  color: 'var(--color-text)',
  textAlign: 'left',
  padding: '7px 14px',
  cursor: 'pointer',
  fontSize: 13,
};

function MenuLock({ title }: { title?: string }) {
  return (
    <span style={{ color: '#f0c060', display: 'flex', flexShrink: 0 }} title={title}>
      <LockIcon />
    </span>
  );
}
