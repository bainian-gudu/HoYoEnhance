import { motion } from 'framer-motion';
import { ArrowUpRight, X } from 'lucide-react';
import type { AppState } from '../hooks/useAppState';
import { GAME_NAV_ITEMS, SHARED_NAV_ITEMS } from '../lib/nav';
import { Brand } from '../components/Brand';
import { GameMark } from '../components/GameIcon';
import { GAME_META, PROJECT_URL } from '../lib/config';

/**
 * 左侧导航：上半部分跟随当前游戏（概览 / 设置 / 使用指南），下半部分是与游戏无关的页面
 * （运行日志 / 关于项目）。移动端为抽屉。
 * 导航项不挂任何悬停气泡：原生 title 与自绘提示都会在窗口挪位 / 隐藏后残留，
 * 也会盖住右侧内容区；页面名已经写在导航项里，不需要再补一层说明。
 */
export function AppSidebar({ app }: { app: AppState }) {
  const { page, sidebarOpen, setSidebarOpen, logs, version, sidebarRef, navigate, activeGame } = app;
  const meta = GAME_META[activeGame];

  return (
    <aside ref={sidebarRef} id="app-navigation" className={`sidebar ${sidebarOpen ? 'sidebar-open' : ''}`} aria-label="主导航" role={sidebarOpen ? 'dialog' : undefined} aria-modal={sidebarOpen || undefined}>
      {/* 品牌区只是标识：不做成按钮，避免左上角出现可聚焦的框，也不再抢「返回概览」这个动作（导航栏第一项即可）。 */}
      <div className="sidebar-brand-row"><Brand /><button className="icon-button sidebar-close" onClick={() => setSidebarOpen(false)} aria-label="关闭导航"><X size={19} /></button></div>
      <div className="nav-group-label game-group-label"><GameMark game={activeGame} size={17} /><span>{meta.name}</span></div>
      <nav className="primary-nav" aria-label={`${meta.name}页面`}>
        {GAME_NAV_ITEMS.map(({ page: itemPage, label, icon: Icon }) => (
          <a key={itemPage} href={`#${itemPage}`} aria-label={`${meta.name} · ${label}`}
            aria-current={page === itemPage ? 'page' : undefined} className={`nav-item ${page === itemPage ? 'active' : ''}`}
            onClick={(event) => { event.preventDefault(); navigate(itemPage); }}>
            {page === itemPage && <motion.span layoutId="active-navigation" className="nav-active-background" transition={{ type: 'spring', stiffness: 400, damping: 36 }} />}
            <Icon size={19} strokeWidth={1.6} /><span>{label}</span>
          </a>
        ))}
      </nav>
      <div className="sidebar-bottom">
        <div className="nav-group-label shared-group-label">共享</div>
        <nav className="secondary-nav" aria-label="共享页面">
          {SHARED_NAV_ITEMS.map(({ page: itemPage, label, icon: Icon }) => (
            <a href={`#${itemPage}`} key={itemPage} aria-label={label}
              aria-current={page === itemPage ? 'page' : undefined} className={`nav-item ${page === itemPage ? 'active' : ''}`}
              onClick={(event) => { event.preventDefault(); navigate(itemPage); }}>
              {page === itemPage && <motion.span layoutId="active-navigation" className="nav-active-background" />}
              <Icon size={18} strokeWidth={1.6} /><span>{label}</span>
              {itemPage === 'logs' && logs.length > 0 && <span className="nav-count">{logs.length > 99 ? '99+' : logs.length}</span>}
            </a>
          ))}
        </nav>
        <div className="sidebar-version"><button onClick={() => navigate('about')}><span className="version-dot" />v{version}</button><a href={`${PROJECT_URL}/blob/main/LICENSE`} target="_blank" rel="noreferrer">MIT License<ArrowUpRight size={11} /></a></div>
      </div>
    </aside>
  );
}
