import { Component } from 'react';
import type { ReactNode } from 'react';

interface Props { children: ReactNode; }
interface State { error: Error | null; }

export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { error: null };
  }

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error) {
    // Post error to C# host so it appears in Log.txt
    try {
      const msg = JSON.stringify({ type: 'toast', level: 'error', title: 'UI Error', message: error.message });
      window.chrome?.webview?.postMessage(msg);
    } catch { /* ignore */ }
    console.error('[ErrorBoundary]', error);
  }

  render() {
    if (this.state.error) {
      return (
        <div style={{
          padding: 24,
          color: '#f87171',
          fontFamily: 'monospace',
          fontSize: 13,
          whiteSpace: 'pre-wrap',
          background: '#1e1e2e',
          minHeight: '100vh',
        }}>
          <strong>UI Error</strong>{'\n\n'}
          {this.state.error.message}{'\n\n'}
          {this.state.error.stack}
        </div>
      );
    }
    return this.props.children;
  }
}
