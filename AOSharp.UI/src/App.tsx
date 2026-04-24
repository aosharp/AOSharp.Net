import { useEffect, useState } from 'react';
import { initBridge } from './store';
import { useStore, selectActiveProfile } from './store';
import { sendToHost } from './bridge';
import { ProfileList } from './components/ProfileList';
import { PluginsGrid } from './components/PluginsGrid';
import { AddPluginDialog } from './components/AddPluginDialog';
import { TweaksDialog } from './components/TweaksDialog';
import { Toaster } from './components/Toaster';

export default function App() {
  const [showAddPlugin, setShowAddPlugin] = useState(false);
  const [showTweaks, setShowTweaks] = useState(false);

  const activeProfile = useStore(selectActiveProfile);
  const isCompiling = useStore((s) => s.isCompiling);
  const compileProgress = useStore((s) => s.compileProgress);

  useEffect(() => {
    initBridge();
  }, []);

  const isInjected = activeProfile?.isInjected ?? false;

  function handleInjectEject() {
    sendToHost(isInjected ? { type: 'eject' } : { type: 'inject' });
  }

  function handleCompileAll() {
    sendToHost({ type: 'compileAll' });
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh' }}>
      {/* Toolbar */}
      <div
        style={{
          height: 40,
          background: 'var(--color-surface)',
          borderBottom: '1px solid var(--color-border)',
          display: 'flex',
          alignItems: 'center',
          padding: '0 8px',
          gap: 6,
          flexShrink: 0,
        }}
      >
        <div style={{ flex: 1 }} />
        {activeProfile && (
          <button
            onClick={handleInjectEject}
            style={{
              ...toolbarBtnStyle,
              background: isInjected ? '#3d1a1a' : 'var(--color-surface-hover)',
              borderColor: isInjected ? 'var(--color-red)' : 'var(--color-border)',
              minWidth: 72,
            }}
          >
            {isInjected ? 'Eject' : 'Inject'}
          </button>
        )}
        <button
          onClick={handleCompileAll}
          disabled={isCompiling}
          style={{ ...toolbarBtnStyle, minWidth: 120, opacity: isCompiling ? 0.6 : 1 }}
        >
          {isCompiling
            ? `Compiling${compileProgress ? `: ${compileProgress.pluginName}` : '...'}`
            : 'Compile Plugins'}
        </button>
        <button
          onClick={() => setShowAddPlugin(true)}
          style={{ ...toolbarBtnStyle, minWidth: 100 }}
        >
          Add Plugin
        </button>
        <button
          onClick={() => setShowTweaks(true)}
          title="Tweaks"
          style={iconBtnStyle}
        >
          ⚙
        </button>
      </div>

      {/* Main content */}
      <div style={{ flex: 1, display: 'flex', overflow: 'hidden' }}>
        <ProfileList />
        <PluginsGrid />
      </div>

      {showAddPlugin && <AddPluginDialog onClose={() => setShowAddPlugin(false)} />}
      {showTweaks && <TweaksDialog onClose={() => setShowTweaks(false)} />}
      <Toaster />
    </div>
  );
}

const toolbarBtnStyle: React.CSSProperties = {
  background: 'var(--color-surface-hover)',
  border: '1px solid var(--color-border)',
  borderRadius: 4,
  color: 'var(--color-text)',
  padding: '4px 10px',
  cursor: 'pointer',
  fontSize: 12,
};

const iconBtnStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  color: 'var(--color-text-muted)',
  cursor: 'pointer',
  fontSize: 16,
  lineHeight: 1,
  padding: '2px 4px',
};
