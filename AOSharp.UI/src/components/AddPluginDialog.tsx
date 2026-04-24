import { useState } from 'react';
import { sendToHost } from '../bridge';
import { useStore } from '../store';
import type { RepoProject } from '../types';

interface Props {
  onClose: () => void;
}

type Tab = 'dll' | 'repo';
type RepoStep = 'url' | 'projects';

export function AddPluginDialog({ onClose }: Props) {
  const [tab, setTab] = useState<Tab>('dll');

  // DLL tab state
  const [dllPath, setDllPath] = useState('');

  // Repo tab state
  const [repoStep, setRepoStep] = useState<RepoStep>('url');
  const [repoUrl, setRepoUrl] = useState('');
  const [isFetching, setIsFetching] = useState(false);
  const [projects, setProjects] = useState<RepoProject[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());

  const requestBrowse = useStore((s) => s.requestBrowse);
  const requestRepoCsprojs = useStore((s) => s.requestRepoCsprojs);

  async function handleBrowseDll() {
    const path = await requestBrowse('dll');
    if (path) setDllPath(path);
  }

  async function handleFetchProjects() {
    const url = repoUrl.trim();
    if (!url) return;
    setIsFetching(true);
    const found = await requestRepoCsprojs(url);
    setProjects(found);
    setSelected(new Set(found.map((p) => p.path)));
    setIsFetching(false);
    setRepoStep('projects');
  }

  function toggleProject(path: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      next.has(path) ? next.delete(path) : next.add(path);
      return next;
    });
  }

  function handleAdd() {
    if (tab === 'dll') {
      if (!dllPath) return;
      sendToHost({ type: 'addDllPlugin', path: dllPath });
    } else {
      const url = repoUrl.trim();
      if (!url || selected.size === 0) return;
      for (const proj of projects) {
        if (selected.has(proj.path)) {
          sendToHost({ type: 'addRepoPlugin', url, projectFilePath: proj.path });
        }
      }
    }
    onClose();
  }

  function handleBack() {
    setRepoStep('url');
    setProjects([]);
    setSelected(new Set());
  }

  const canAdd =
    tab === 'dll' ? !!dllPath : repoStep === 'projects' && selected.size > 0;

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        background: 'rgba(0,0,0,0.6)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 500,
      }}
      onClick={onClose}
    >
      <div
        style={{
          background: 'var(--color-surface)',
          border: '1px solid var(--color-border)',
          borderRadius: 6,
          minWidth: 440,
          maxWidth: 560,
          width: '100%',
          padding: '20px',
          boxShadow: '0 8px 32px rgba(0,0,0,0.5)',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <h3 style={{ margin: '0 0 16px', fontSize: 15 }}>Add Plugin</h3>

        {/* Tab bar */}
        <div style={{ display: 'flex', gap: 6, marginBottom: 16 }}>
          <TabBtn active={tab === 'dll'} onClick={() => { setTab('dll'); }}>DLL Path</TabBtn>
          <TabBtn active={tab === 'repo'} onClick={() => { setTab('repo'); }}>Repository URL</TabBtn>
        </div>

        {/* DLL tab */}
        {tab === 'dll' && (
          <div style={{ display: 'flex', gap: 6, marginBottom: 16 }}>
            <input
              readOnly
              value={dllPath}
              placeholder="Select a .dll file..."
              style={inputStyle}
            />
            <button onClick={handleBrowseDll} style={btnStyle}>...</button>
          </div>
        )}

        {/* Repo tab – Step 1: URL */}
        {tab === 'repo' && repoStep === 'url' && (
          <div style={{ marginBottom: 16 }}>
            <label style={labelStyle}>Repository URL</label>
            <input
              value={repoUrl}
              onChange={(e) => setRepoUrl(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') handleFetchProjects(); }}
              placeholder="https://github.com/..."
              style={{ ...inputStyle, marginBottom: 0 }}
              autoFocus
            />
          </div>
        )}

        {/* Repo tab – Step 2: Project checklist */}
        {tab === 'repo' && repoStep === 'projects' && (
          <div style={{ marginBottom: 16 }}>
            <div style={{ fontSize: 12, color: 'var(--color-text-muted)', marginBottom: 8 }}>
              Select projects to add from <span style={{ color: 'var(--color-text)' }}>{repoUrl}</span>
            </div>
            <div
              style={{
                border: '1px solid var(--color-border)',
                borderRadius: 4,
                maxHeight: 180,
                overflowY: 'auto',
                marginBottom: 12,
              }}
            >
              {projects.length === 0 ? (
                <div style={{ padding: '10px 12px', fontSize: 13, color: 'var(--color-text-muted)' }}>
                  No .csproj files found.
                </div>
              ) : (
                projects.map((proj) => (
                  <label
                    key={proj.path}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 10,
                      padding: '7px 12px',
                      cursor: 'pointer',
                      borderBottom: '1px solid var(--color-border)',
                      fontSize: 13,
                    }}
                    onMouseEnter={(e) =>
                      (e.currentTarget.style.background = 'var(--color-surface-hover)')
                    }
                    onMouseLeave={(e) =>
                      (e.currentTarget.style.background = 'transparent')
                    }
                  >
                    <input
                      type="checkbox"
                      checked={selected.has(proj.path)}
                      onChange={() => toggleProject(proj.path)}
                      style={{ cursor: 'pointer' }}
                    />
                    <span style={{ flex: 1 }}>{proj.name}</span>
                    {proj.isLibrary && (
                      <span style={{
                        fontSize: 10,
                        fontWeight: 600,
                        letterSpacing: '0.04em',
                        color: 'var(--color-accent)',
                        border: '1px solid var(--color-accent)',
                        borderRadius: 3,
                        padding: '1px 5px',
                      }}>
                        LIBRARY
                      </span>
                    )}
                  </label>
                ))
              )}
            </div>
          </div>
        )}

        {/* Spinner while fetching */}
        {tab === 'repo' && isFetching && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 16, fontSize: 13, color: 'var(--color-text-muted)' }}>
            <div className="spinner" style={{ width: 16, height: 16, borderWidth: 2 }} />
            Cloning repository…
          </div>
        )}

        {/* Footer buttons */}
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
          {tab === 'repo' && repoStep === 'url' && (
            <button
              onClick={handleFetchProjects}
              disabled={isFetching || !repoUrl.trim()}
              style={{ ...btnStyle, minWidth: 80, background: 'var(--color-accent)', opacity: (!repoUrl.trim() || isFetching) ? 0.5 : 1 }}
            >
              Next
            </button>
          )}
          {tab === 'repo' && repoStep === 'projects' && (
            <>
              <button
                onClick={handleAdd}
                disabled={selected.size === 0}
                style={{ ...btnStyle, minWidth: 80, background: 'var(--color-accent)', opacity: selected.size === 0 ? 0.5 : 1 }}
              >
                Add
              </button>
              <button onClick={handleBack} style={{ ...btnStyle, minWidth: 80 }}>
                Back
              </button>
            </>
          )}
          {tab === 'dll' && (
            <button
              onClick={handleAdd}
              disabled={!canAdd}
              style={{ ...btnStyle, minWidth: 80, background: 'var(--color-accent)', opacity: !canAdd ? 0.5 : 1 }}
            >
              Add
            </button>
          )}
          <button onClick={onClose} style={{ ...btnStyle, minWidth: 80 }}>
            Cancel
          </button>
        </div>
      </div>
    </div>
  );
}

function TabBtn({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      onClick={onClick}
      style={{
        flex: 1,
        padding: '6px 0',
        background: active ? 'var(--color-accent)' : 'var(--color-bg)',
        border: '1px solid var(--color-border)',
        borderRadius: 4,
        color: 'var(--color-text)',
        fontWeight: active ? 700 : 400,
        cursor: 'pointer',
        fontSize: 13,
      }}
    >
      {children}
    </button>
  );
}

const inputStyle: React.CSSProperties = {
  flex: 1,
  background: 'var(--color-bg)',
  border: '1px solid var(--color-border)',
  borderRadius: 4,
  color: 'var(--color-text)',
  padding: '6px 8px',
  fontSize: 13,
  width: '100%',
};

const btnStyle: React.CSSProperties = {
  background: 'var(--color-surface-hover)',
  border: '1px solid var(--color-border)',
  borderRadius: 4,
  color: 'var(--color-text)',
  padding: '6px 12px',
  cursor: 'pointer',
  fontSize: 13,
};

const labelStyle: React.CSSProperties = {
  display: 'block',
  fontSize: 12,
  color: 'var(--color-text-muted)',
  marginBottom: 4,
};
