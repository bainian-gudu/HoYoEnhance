import { useCallback, useEffect, useRef, useState } from 'react';
import type { GameId, Page, Theme } from '../lib/config';
import { APP_NAME, GAME_META, getPage } from '../lib/config';
import { clearKeyboardFocus, clearTabFocus, markKeyboardFocus } from '../lib/focus';
import { PAGE_NAMES, isGamePage } from '../lib/nav';
import { nativeInvoke, onNativeNavigate } from '../lib/native';

/** 页面、主题、侧栏与键盘/焦点行为；只负责浏览器外壳，不碰业务配置。 */
export function useAppChrome({ native, displayGame, modalOpen }: {
  native: boolean;
  displayGame: GameId;
  modalOpen: boolean;
}) {
  const [page, setPage] = useState<Page>(getPage);
  const [theme, setTheme] = useState<Theme>(() => {
    try { return localStorage.getItem('genshin-fps-unlocker.theme') === 'dark' ? 'dark' : 'light'; } catch { return 'light'; }
  });
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const sidebarRef = useRef<HTMLElement>(null);

  const navigate = useCallback((next: Page) => {
    setPage(next);
    setSidebarOpen(false);
    window.location.hash = next;
    window.scrollTo({ top: 0, behavior: 'auto' });
  }, []);

  useEffect(() => {
    const onHashChange = () => {
      setPage(getPage());
      setSidebarOpen(false);
      window.scrollTo({ top: 0, behavior: 'auto' });
    };
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  // 主界面不参与 Tab 焦点遍历：按下 Tab 直接吞掉，焦点不移动、界面没有任何反应。
  // 键盘焦点标记（focus-visible 外框）改由标签页方向键这类显式键盘操作触发；
  // 鼠标点击、失焦和隐藏窗口仍会清掉标记。
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Tab') return;
      event.preventDefault();
      event.stopPropagation();
    };
    const onPointerDown = () => clearKeyboardFocus();
    const onVisibilityChange = () => {
      if (document.visibilityState !== 'hidden') return;
      clearKeyboardFocus();
      clearTabFocus();
    };
    const onWindowBlur = () => {
      clearKeyboardFocus();
      clearTabFocus();
    };
    const onWindowFocus = () => {
      const el = document.activeElement;
      if (el instanceof HTMLElement
        && !el.matches('input, textarea, select')
        && el.matches(':focus-visible')) markKeyboardFocus();
    };
    window.addEventListener('keydown', onKeyDown, true);
    window.addEventListener('pointerdown', onPointerDown, true);
    window.addEventListener('blur', onWindowBlur);
    window.addEventListener('focus', onWindowFocus);
    document.addEventListener('visibilitychange', onVisibilityChange);
    return () => {
      window.removeEventListener('keydown', onKeyDown, true);
      window.removeEventListener('pointerdown', onPointerDown, true);
      window.removeEventListener('blur', onWindowBlur);
      window.removeEventListener('focus', onWindowFocus);
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };
  }, []);

  // 宿主在窗口进托盘（最小化 / 关窗）时发 navigate，把界面复位到「游戏概览」。
  useEffect(() => {
    if (!native) return;
    const offNavigate = onNativeNavigate((next) => {
      clearKeyboardFocus();
      clearTabFocus();
      navigate(next);
    });
    return () => { offNavigate(); };
  }, [native, navigate]);

  useEffect(() => {
    if (!sidebarOpen) return;
    const query = window.matchMedia('(max-width: 560px)');
    if (!query.matches) { setSidebarOpen(false); return; }
    const previousFocus = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    const pane = document.querySelector<HTMLElement>('.main-pane');
    if (pane) pane.inert = true;
    document.body.style.overflow = 'hidden';
    const frame = requestAnimationFrame(() => sidebarRef.current?.querySelector<HTMLElement>('.nav-item.active')?.focus());
    const onResize = () => { if (!query.matches) setSidebarOpen(false); };
    const onTab = (event: KeyboardEvent) => {
      if (event.key !== 'Tab') return;
      const elements = Array.from(sidebarRef.current?.querySelectorAll<HTMLElement>('a[href], button') ?? [])
        .filter((element) => element.getClientRects().length > 0);
      const first = elements[0];
      const last = elements[elements.length - 1];
      if (!first) return;
      if (!sidebarRef.current?.contains(document.activeElement)) { event.preventDefault(); first.focus(); }
      else if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    query.addEventListener('change', onResize);
    document.addEventListener('keydown', onTab);
    return () => {
      cancelAnimationFrame(frame);
      if (pane) pane.inert = false;
      document.body.style.overflow = previousOverflow;
      query.removeEventListener('change', onResize);
      document.removeEventListener('keydown', onTab);
      if (previousFocus?.isConnected) previousFocus.focus();
    };
  }, [sidebarOpen]);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme === 'dark' ? '#121319' : '#f5f5f8');
    try { localStorage.setItem('genshin-fps-unlocker.theme', theme); } catch { /* ignore */ }
    if (native) {
      void nativeInvoke('setUiTheme', { theme }).catch(() => undefined);
    }
  }, [theme, native]);

  useEffect(() => {
    const scope = isGamePage(page) ? ` · ${GAME_META[displayGame].short}` : '';
    document.title = `${PAGE_NAMES[page]}${scope} | ${APP_NAME}`;
  }, [page, displayGame]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement;
      if (target.matches('input, textarea, select') || target.isContentEditable || modalOpen) return;
      if ((event.ctrlKey || event.metaKey) && event.key === ',') { event.preventDefault(); navigate('settings'); }
      if (event.key === 'Escape') setSidebarOpen(false);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [modalOpen, navigate]);

  return { page, theme, setTheme, sidebarOpen, setSidebarOpen, sidebarRef, navigate };
}
