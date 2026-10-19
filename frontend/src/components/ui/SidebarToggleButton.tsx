type SidebarToggleButtonProps = {
  onClick: () => void;
  className?: string;
};

export function SidebarToggleButton({ onClick, className = "" }: SidebarToggleButtonProps) {
  return (
    <button type="button" onClick={onClick} aria-label="Toggle sidebar"
      aria-controls="app-sidebar" title="Toggle sidebar"
      className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg text-slate-500 
        transition hover:bg-slate-100 hover:text-slate-800 focus:outline-none focus-visible:ring-4 
        focus-visible:ring-violet-100 ${className}`}
    >
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"
        strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        <rect width="18" height="18" x="3" y="3" rx="2" />
        <path d="M9 3v18" />
      </svg>
    </button>
  );
}