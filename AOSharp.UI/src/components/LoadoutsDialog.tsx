import { useEffect, useMemo, useState, type CSSProperties } from 'react';
import { sendToHost } from '../bridge';
import { selectPluginsMap, useStore } from '../store';
import { getPluginSection, PLUGIN_SECTIONS, type PluginSection } from '../pluginSections';
import type { Loadout } from '../types';
import { LockIcon } from './LockIcon';
import { CrossIcon } from './PluginGridIcons';

interface Props {
  onClose: () => void;
}

export function LoadoutsDialog({ onClose }: Props) {
  const loadouts = useStore((s) => s.loadouts);
  const pluginsMap = useStore(selectPluginsMap);

  const [selectedId, setSelectedId] = useState<string | null>(loadouts[0]?.id ?? null);
  const [editName, setEditName] = useState('');
  const [editKeys, setEditKeys] = useState<Set<string>>(new Set());
  const [loadoutFilter, setLoadoutFilter] = useState('');
  const [pluginFilter, setPluginFilter] = useState('');
  const [newName, setNewName] = useState('');

  const selected = loadouts.find((l) => l.id === selectedId) ?? null;

  useEffect(() => {
    if (loadouts.length > 0 && (!selectedId || !loadouts.some((l) => l.id === selectedId))) {
      setSelectedId(loadouts[0].id);
    }
  }, [loadouts, selectedId]);

  useEffect(() => {
    if (selected) {
      setEditName(selected.name);
      setEditKeys(new Set(selected.pluginKeys));
    }
  }, [selected?.id, selected?.name, selected?.pluginKeys.join(',')]);

  const injectablePlugins = useMemo(() => {
    const entries: [string, (typeof pluginsMap)[string]][] = [];
    for (const [key, plugin] of Object.entries(pluginsMap)) {
      if (!plugin.isLibrary) entries.push([key, plugin]);
    }
    return entries.sort(([, a], [, b]) =>
      a.name.localeCompare(b.name, undefined, { sensitivity: 'base' })
    );
  }, [pluginsMap]);

  const pluginsBySection = useMemo(() => {
    const grouped = new Map<PluginSection, [string, (typeof pluginsMap)[string]][]>();
    for (const section of PLUGIN_SECTIONS) grouped.set(section, []);
    const q = pluginFilter.trim().toLowerCase();
    for (const entry of injectablePlugins) {
      if (q && !entry[1].name.toLowerCase().includes(q)) continue;
      const section = getPluginSection(entry[1]);
      grouped.get(section)!.push(entry);
    }
    return grouped;
  }, [injectablePlugins, pluginFilter]);

  const isDirty =
    selected &&
    (editName !== selected.name ||
      editKeys.size !== selected.pluginKeys.length ||
      selected.pluginKeys.some((k) => !editKeys.has(k)));

  function handleSave() {
    if (!selected || selected.isLocked) return;
    sendToHost({
      type: 'updateLoadout',
      loadoutId: selected.id,
      name: editName.trim() || selected.name,
      pluginKeys: Array.from(editKeys),
    });
  }

  function handleCreate() {
    const name = newName.trim();
    if (!name) return;
    sendToHost({ type: 'createLoadout', name });
    setNewName('');
  }

  function handleDelete(loadout: Loadout) {
    if (loadout.isDefault || loadout.isLocked) return;
    if (!confirm(`Delete loadout "${loadout.name}"?`)) return;
    sendToHost({ type: 'deleteLoadout', loadoutId: loadout.id });
    if (selectedId === loadout.id) {
      setSelectedId(loadouts.find((l) => l.isDefault)?.id ?? loadouts[0]?.id ?? null);
    }
  }

  function toggleKey(key: string) {
    if (selected?.isLocked) return;
    setEditKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  return (
    <div style={overlayStyle} onClick={onClose}>
      <div style={panelStyle} onClick={(e) => e.stopPropagation()}>
          <div
            style={{
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              marginBottom: 16,
              flexShrink: 0,
            }}
          >
            <h3 style={{ margin: 0, fontSize: 15 }}>Manage loadouts</h3>
            <button
              type="button"
              onClick={onClose}
              style={{ ...ghostBtnStyle, display: 'inline-flex', alignItems: 'center', justifyContent: 'center' }}
              aria-label="Close"
            >
              <CrossIcon size={14} />
            </button>
          </div>

          <div style={{ display: 'flex', gap: 12, flex: 1, minHeight: 0, overflow: 'hidden' }}>
            <div style={listColStyle}>
              <input
                placeholder="Filter loadouts…"
                value={loadoutFilter}
                onChange={(e) => setLoadoutFilter(e.target.value)}
                style={{ ...inputStyle, marginBottom: 8 }}
              />
              <div style={{ flex: 1, overflowY: 'auto', marginBottom: 8 }}>
                {loadouts
                  .filter(
                    (l) =>
                      !loadoutFilter.trim() ||
                      l.name.toLowerCase().includes(loadoutFilter.trim().toLowerCase())
                  )
                  .map((l) => (
                    <LoadoutListItem
                      key={l.id}
                      loadout={l}
                      isSelected={l.id === selectedId}
                      onSelect={() => setSelectedId(l.id)}
                      onDelete={() => handleDelete(l)}
                    />
                  ))}
              </div>
              <div style={{ display: 'flex', gap: 6, marginBottom: 8 }}>
                <input
                  placeholder="New loadout name…"
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleCreate()}
                  style={{ ...inputStyle, flex: 1 }}
                />
                <button type="button" onClick={handleCreate} disabled={!newName.trim()} style={btnStyle}>
                  + New
                </button>
              </div>
            </div>

            <div style={editorColStyle}>
              {!selected ? (
                <p style={{ color: 'var(--color-text-muted)', fontSize: 13 }}>Select a loadout</p>
              ) : (
                <>
                  {selected.isLocked && (
                    <div style={lockBannerStyle}>
                      This loadout is in use on an injected character. Eject to edit.
                    </div>
                  )}
                  <label style={labelStyle}>Name</label>
                  <input
                    value={editName}
                    onChange={(e) => setEditName(e.target.value)}
                    disabled={selected.isLocked}
                    style={{ ...inputStyle, marginBottom: 12 }}
                  />
                  <label style={labelStyle}>Plugins in loadout</label>
                  <input
                    placeholder="Filter plugins…"
                    value={pluginFilter}
                    onChange={(e) => setPluginFilter(e.target.value)}
                    style={{ ...inputStyle, marginBottom: 8 }}
                  />
                  <div style={{ flex: 1, overflowY: 'auto', minHeight: 0 }}>
                    {PLUGIN_SECTIONS.map((section) => {
                      const plugins = pluginsBySection.get(section)!;
                      if (plugins.length === 0) return null;
                      return (
                        <div key={section} style={{ marginBottom: 12 }}>
                          <span style={sectionLabelStyle}>{section}</span>
                          {plugins.map(([key, plugin]) => (
                            <label
                              key={key}
                              style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: 8,
                                padding: '4px 0',
                                fontSize: 12,
                                cursor: selected.isLocked ? 'default' : 'pointer',
                                opacity: selected.isLocked ? 0.6 : 1,
                              }}
                            >
                              <input
                                type="checkbox"
                                checked={editKeys.has(key)}
                                disabled={selected.isLocked}
                                onChange={() => toggleKey(key)}
                              />
                              {plugin.name}
                            </label>
                          ))}
                        </div>
                      );
                    })}
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 12, flexShrink: 0 }}>
                    <button
                      type="button"
                      onClick={handleSave}
                      disabled={selected.isLocked || !isDirty}
                      style={{
                        ...btnStyle,
                        background: 'var(--color-accent)',
                        borderColor: 'var(--color-accent)',
                        color: '#fff',
                        opacity: selected.isLocked || !isDirty ? 0.5 : 1,
                      }}
                    >
                      Save
                    </button>
                  </div>
                </>
              )}
            </div>
          </div>
        </div>
      </div>
  );
}

function TrashIcon() {
  return (
    <svg
      width="15"
      height="15"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      <path d="M3 6h18" />
      <path d="M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2" />
      <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
      <path d="M10 11v6M14 11v6" />
    </svg>
  );
}

function LoadoutListItem({
  loadout,
  isSelected,
  onSelect,
  onDelete,
}: {
  loadout: Loadout;
  isSelected: boolean;
  onSelect: () => void;
  onDelete: () => void;
}) {
  const canDelete = !loadout.isDefault && !loadout.isLocked;
  const [deleteHover, setDeleteHover] = useState(false);

  const deleteIconStyle: CSSProperties = {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    marginRight: 6,
    padding: 4,
    border: 'none',
    background: 'transparent',
    cursor: canDelete ? 'pointer' : 'not-allowed',
    color: canDelete ? (deleteHover ? '#fca5a5' : '#f87171') : 'var(--color-text-muted)',
    opacity: canDelete ? 1 : 0.4,
  };

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 4,
        marginBottom: 2,
        borderRadius: 4,
        background: isSelected ? 'var(--color-surface-hover)' : 'transparent',
      }}
    >
      <button
        type="button"
        onClick={onSelect}
        style={{
          flex: 1,
          minWidth: 0,
          display: 'flex',
          alignItems: 'center',
          gap: 6,
          textAlign: 'left',
          padding: '8px 6px 8px 10px',
          border: 'none',
          borderRadius: 4,
          background: 'transparent',
          color: loadout.isLocked ? 'var(--color-text-muted)' : 'var(--color-text)',
          cursor: 'pointer',
          fontSize: 13,
        }}
      >
        <span
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: 6,
            minWidth: 0,
            flex: 1,
            overflow: 'hidden',
          }}
        >
          <span
            style={{
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              minWidth: 0,
            }}
          >
            {loadout.name}
          </span>
          {loadout.isLocked && (
            <span
              style={{ color: '#f0c060', display: 'flex', flexShrink: 0 }}
              title="In use on injected character"
            >
              <LockIcon />
            </span>
          )}
        </span>
      </button>
      {!loadout.isDefault && (
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation();
            onDelete();
          }}
          disabled={!canDelete}
          onMouseEnter={() => canDelete && setDeleteHover(true)}
          onMouseLeave={() => setDeleteHover(false)}
          title={
            loadout.isLocked
              ? 'Eject before deleting'
              : canDelete
                ? `Delete ${loadout.name}`
                : undefined
          }
          style={deleteIconStyle}
        >
          <TrashIcon />
        </button>
      )}
    </div>
  );
}

const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0,0,0,0.6)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  zIndex: 500,
};

const panelStyle: React.CSSProperties = {
  background: 'var(--color-surface)',
  border: '1px solid var(--color-border)',
  borderRadius: 8,
  width: 'min(720px, 92vw)',
  height: 'min(520px, 85vh)',
  padding: 20,
  boxShadow: '0 8px 32px rgba(0,0,0,0.5)',
  display: 'flex',
  flexDirection: 'column',
};

const listColStyle: React.CSSProperties = {
  width: '35%',
  minWidth: 140,
  minHeight: 0,
  display: 'flex',
  flexDirection: 'column',
  borderRight: '1px solid var(--color-border)',
  paddingRight: 12,
};

const editorColStyle: React.CSSProperties = {
  flex: 1,
  display: 'flex',
  flexDirection: 'column',
  minWidth: 0,
  paddingLeft: 4,
  overflow: 'hidden',
};

const labelStyle: React.CSSProperties = {
  display: 'block',
  fontSize: 11,
  fontWeight: 600,
  color: 'var(--color-text-muted)',
  marginBottom: 4,
  textTransform: 'uppercase',
  letterSpacing: '0.05em',
};

const sectionLabelStyle: React.CSSProperties = {
  fontSize: 10,
  fontWeight: 700,
  letterSpacing: '0.06em',
  textTransform: 'uppercase',
  color: 'var(--color-text-muted)',
  marginBottom: 4,
  display: 'block',
};

const inputStyle: React.CSSProperties = {
  width: '100%',
  background: 'var(--color-bg)',
  border: '1px solid var(--color-border)',
  borderRadius: 4,
  color: 'var(--color-text)',
  padding: '6px 8px',
  fontSize: 12,
  boxSizing: 'border-box',
};

const btnStyle: React.CSSProperties = {
  background: 'var(--color-surface-hover)',
  border: '1px solid var(--color-border)',
  borderRadius: 4,
  color: 'var(--color-text)',
  padding: '6px 12px',
  cursor: 'pointer',
  fontSize: 12,
  flexShrink: 0,
};

const ghostBtnStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  color: 'var(--color-text-muted)',
  cursor: 'pointer',
  fontSize: 16,
  lineHeight: 1,
};

const lockBannerStyle: React.CSSProperties = {
  background: '#3d2a0a',
  border: '1px solid #a06020',
  borderRadius: 4,
  color: '#f0c060',
  padding: '8px 10px',
  fontSize: 12,
  marginBottom: 12,
};
