import { useEffect, useState } from 'react';
import { initBridge } from './store';
import { useStore, selectUiLocked } from './store';
import { sendToHost } from './bridge';
import { ProfileList } from './components/ProfileList';
import { LoadoutBar } from './components/LoadoutBar';
import { PluginsGrid } from './components/PluginsGrid';
import { AddPluginDialog } from './components/AddPluginDialog';
import { TweaksDialog } from './components/TweaksDialog';
import { LoadoutsDialog } from './components/LoadoutsDialog';
import { Toaster } from './components/Toaster';
import { CompilingOverlay } from './components/CompilingOverlay';
import { InjectingOverlay } from './components/InjectingOverlay';
import { RefreshIcon } from './components/PluginGridIcons';

export default function App() {
  const [showAddPlugin, setShowAddPlugin] = useState(false);
  const [showTweaks, setShowTweaks] = useState(false);
  const [showManageLoadouts, setShowManageLoadouts] = useState(false);
  const [isCheckingUpdates, setIsCheckingUpdates] = useState(false);

  const isCompiling = useStore((s) => s.isCompiling);
  const isInjecting = useStore((s) => s.isInjecting);
  const injectQueue = useStore((s) => s.injectQueue);
  const uiLocked = useStore(selectUiLocked);
  const compileProgress = useStore((s) => s.compileProgress);
  const hasUncompiled = useStore((s) =>
    Object.values(s.plugins).some((p) => p.pluginType === 'Repo' && !p.isCompiled)
  );

  useEffect(() => {
    initBridge();
  }, []);

  useEffect(() => {
    if (!uiLocked) return;
    setShowAddPlugin(false);
    setShowTweaks(false);
    setShowManageLoadouts(false);
  }, [uiLocked]);

  function handleCompileAll() {
    if (uiLocked) return;
    sendToHost({ type: 'compileAll' });
  }

  function handleCheckUpdates() {
    if (uiLocked || isCheckingUpdates) return;
    setIsCheckingUpdates(true);
    sendToHost({ type: 'checkUpdates' });
    setTimeout(() => setIsCheckingUpdates(false), 3000);
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh' }}>
      <div style={{
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
        <button
          onClick={() => !uiLocked && setShowTweaks(true)}
          disabled={uiLocked}
          title="Tweaks"
          style={{ ...iconBtnStyle, opacity: uiLocked ? 0.45 : 1, cursor: uiLocked ? 'not-allowed' : 'pointer' }}
        >
          ⚙
        </button>
        <div style={{ flex: 1 }} />
        <button
          type="button"
          onClick={() => !uiLocked && setShowManageLoadouts(true)}
          disabled={uiLocked}
          style={{
            ...primaryToolbarBtnStyle,
            opacity: uiLocked ? 0.45 : 1,
            cursor: uiLocked ? 'not-allowed' : 'pointer',
          }}
        >
          Manage loadouts
        </button>
        {(hasUncompiled || isCompiling) && (
          <button
            onClick={handleCompileAll}
            disabled={isCompiling}
            title={
              isCompiling
                ? `Compiling${compileProgress ? `: ${compileProgress.pluginName}` : '...'}`
                : undefined
            }
            style={{
              ...primaryToolbarBtnStyle,
              opacity: isCompiling ? 0.5 : 1,
              cursor: isCompiling ? 'not-allowed' : 'pointer',
            }}
          >
            {isCompiling
              ? `Compiling${compileProgress ? `: ${compileProgress.pluginName}` : '...'}`
              : 'Compile All'}
          </button>
        )}
        <button
          onClick={() => !uiLocked && setShowAddPlugin(true)}
          disabled={uiLocked}
          style={{
            ...primaryToolbarBtnStyle,
            opacity: uiLocked ? 0.45 : 1,
            cursor: uiLocked ? 'not-allowed' : 'pointer',
          }}
        >
          Install Plugin
        </button>
        <button
          onClick={handleCheckUpdates}
          disabled={uiLocked || isCheckingUpdates}
          title="Check for updates"
          style={{
            ...iconBtnStyle,
            fontSize: 15,
            opacity: uiLocked || isCheckingUpdates ? 0.5 : 1,
            cursor: uiLocked ? 'not-allowed' : 'pointer',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            width: 26,
            height: 26,
          }}
        >
          <span
            style={{
              display: 'inline-flex',
              animation: isCheckingUpdates ? 'spin 1s linear infinite' : 'none',
            }}
          >
            <RefreshIcon size={15} />
          </span>
        </button>
      </div>

      <div style={{ flex: 1, display: 'flex', overflow: 'hidden' }}>
        <ProfileList />
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden', minWidth: 0 }}>
          <LoadoutBar />
          <PluginsGrid />
        </div>
      </div>

      {showAddPlugin && !uiLocked && <AddPluginDialog onClose={() => setShowAddPlugin(false)} />}
      {showTweaks && !uiLocked && <TweaksDialog onClose={() => setShowTweaks(false)} />}
      {showManageLoadouts && !uiLocked && (
        <LoadoutsDialog onClose={() => setShowManageLoadouts(false)} />
      )}
      <Toaster />
      {isCompiling && <CompilingOverlay progress={compileProgress} />}
      {isInjecting && injectQueue.length > 0 && <InjectingOverlay queue={injectQueue} />}
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

const primaryToolbarBtnStyle: React.CSSProperties = {
  ...toolbarBtnStyle,
  color: '#fff',
  fontSize: 11,
  letterSpacing: '0.04em',
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
