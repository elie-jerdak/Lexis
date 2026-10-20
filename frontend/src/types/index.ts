export type DocumentStatus =
  | "Pending"
  | "Processing"
  | "Indexed"
  | "Chunked"
  | "Failed";

export type DocumentItem = {
  id: string; name: string; type: string; size: string; updated: string;
  status: DocumentStatus; color: string;
};

export type DocumentDto = {
  id: string; fileName: string; mimeType: string; fileSize: number;
  status: string | number; uploadedAt: string;
};

export type ConversationDto = { id: string; title: string; createdAt: string; updatedAt: string };

export type RagCitation = {
  referenceId: string; chunkId: string; documentId: string;
  startPage: number; endPage: number; heading: string | null; headingPath: string | null;
};

export type RagQueryResponse = { answer: string; insufficientEvidence: boolean; citations: RagCitation[] };

export type ChatMessage = {
  id: string; role: "user" | "assistant"; content: string;
  createdAt?: string;
  citations?: RagCitation[]; insufficientEvidence?: boolean;
};

export type NavItem = "Overview" | "Chat" | "Usage Logs";

export const navItems = [
  { label: "Overview", icon: "grid" },
  { label: "Chat", icon: "message" },
  { label: "Usage Logs", icon: "chart-bar" },
] as const;

