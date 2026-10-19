import type { DocumentDto, DocumentItem, DocumentStatus } from "@/types";

const documentColors = ["violet", "blue", "amber", "rose"];

export function formatFileSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return "Unknown size";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function formatDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat("en", { month: "short", day: "numeric", year: "numeric" }).format(date);
}

export function normalizeStatus(value: string | number): DocumentStatus {
  if (typeof value === "number") {
    const numericStatuses: Record<number, DocumentStatus> = { 0: "Pending", 1: "Processing", 2: "Indexed", 3: "Failed", 4: "Chunked" };
    return numericStatuses[value] ?? "Pending";
  }
  switch (value.toLowerCase()) {
    case "pending": return "Pending";
    case "processing": return "Processing";
    case "indexed": return "Indexed";
    case "chunked": return "Chunked";
    case "failed": return "Failed";
    default: return "Pending";
  }
}

export function mapDocument(dto: DocumentDto, index: number): DocumentItem {
  return { id: dto.id, name: dto.fileName, type: dto.mimeType || "Document", size: formatFileSize(dto.fileSize), updated: formatDate(dto.uploadedAt), status: normalizeStatus(dto.status), color: documentColors[index % documentColors.length] };
}
