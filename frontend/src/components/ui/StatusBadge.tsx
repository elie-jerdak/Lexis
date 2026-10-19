import type { DocumentStatus } from "@/types";

export function StatusBadge({ status }: { status: DocumentStatus }) {
  const styles: Record<DocumentStatus, string> = {
    Pending: "bg-slate-100 text-slate-700 ring-slate-500/15",
    Processing: "bg-amber-50 text-amber-700 ring-amber-600/15",
    Indexed: "bg-emerald-50 text-emerald-700 ring-emerald-600/15",
    Chunked: "bg-blue-50 text-blue-700 ring-blue-600/15",
    Failed: "bg-rose-50 text-rose-700 ring-rose-600/15",
  };

  const dotStyles: Record<DocumentStatus, string> = {
    Pending: "bg-slate-400",
    Processing: "bg-amber-500",
    Indexed: "bg-emerald-500",
    Chunked: "bg-blue-500",
    Failed: "bg-rose-500",
  };

  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ring-1 ring-inset ${styles[status]}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${dotStyles[status]}`} />
      {status}
    </span>
  );
}

