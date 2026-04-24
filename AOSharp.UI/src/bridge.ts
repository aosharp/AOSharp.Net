import type { OutboundMessage } from './types';

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (msg: string) => void;
        addEventListener: (event: 'message', handler: (e: { data: string }) => void) => void;
        removeEventListener: (event: 'message', handler: (e: { data: string }) => void) => void;
      };
    };
  }
}

/** Send a typed action to the C# host. No-ops in browser dev mode. */
export function sendToHost(msg: OutboundMessage): void {
  const json = JSON.stringify(msg);
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage(json);
  } else {
    console.debug('[bridge → host]', msg);
  }
}

/** Register a raw message listener from C#. Returns cleanup fn. */
export function onHostMessage(handler: (data: string) => void): () => void {
  const wrapper = (e: { data: string }) => handler(e.data);
  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener('message', wrapper);
    return () => window.chrome!.webview!.removeEventListener('message', wrapper);
  }
  return () => {};
}
