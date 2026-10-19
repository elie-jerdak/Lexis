"use client";

import { useEffect, useMemo, useState, type FormEvent, type RefObject } from "react";
import type { ChatMessage, DocumentItem } from "@/types";
import { Icon } from "@/components/ui/Icon";

type ChatPanelProps = {
  documents: DocumentItem[];
  conversationId: string | null;
  conversationTitle: string;
  messages: ChatMessage[];
  chatInput: string;
  isLoadingConversation: boolean;
  isCreatingConversation: boolean;
  isSendingMessage: boolean;
  isUploading: boolean;
  chatEndRef: RefObject<HTMLDivElement | null>;
  selectedDocumentIds: string[];
  setChatInput: (value: string) => void;
  onToggleDocument: (id: string, checked: boolean) => void;
  onStartNewConversation: () => void;
  onUploadDocument: () => void;
  onSendMessage: (event: FormEvent<HTMLFormElement>) => void;
  onStopMessage: () => void;
  onEditMessage: (message: ChatMessage) => void;
  onCopyMessage: (content: string) => Promise<boolean>;
  onRetryMessage: (message: ChatMessage) => void;
};

const actionButtonClass =
  "flex h-8 w-8 items-center justify-center rounded-md text-slate-400 transition hover:bg-slate-100 hover:text-slate-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-violet-300 disabled:cursor-not-allowed disabled:opacity-40 sm:h-7 sm:w-7";

function ActionIcon({ children }: { children: React.ReactNode }) {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  );
}

const CopyIcon = () => (
  <ActionIcon>
    <rect width="14" height="14" x="8" y="8" rx="2" ry="2" />
    <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" />
  </ActionIcon>
);
const CheckIcon = () => (
  <ActionIcon><path d="M20 6 9 17l-5-5" /></ActionIcon>
);
const EditIcon = () => (
  <ActionIcon>
    <path d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z" />
    <path d="m15 5 4 4" />
  </ActionIcon>
);
const RetryIcon = () => (
  <ActionIcon>
    <path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" />
    <path d="M3 3v5h5" />
  </ActionIcon>
);

// The API returns UTC times without a "Z"; without one, JS reads them as local time
// and every message would look hours old.
function parseServerDate(value?: string): Date | null {
  if (!value) return null;
  const hasZone = /(?:[zZ]|[+-]\d{2}:?\d{2})$/.test(value);
  const date = new Date(hasZone ? value : `${value}Z`);
  return Number.isNaN(date.getTime()) ? null : date;
}

function formatRelativeTime(date: Date, now: number): string {
  const seconds = Math.max(0, Math.floor((now - date.getTime()) / 1000));
  if (seconds < 45) return "just now";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  if (days < 7) return `${days}d ago`;
  return date.toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
    year: new Date(now).getFullYear() !== date.getFullYear() ? "numeric" : undefined,
  });
}

function MessageTime({ value, now }: { value?: string; now: number }) {
  const date = parseServerDate(value);
  if (!date) return null;
  return (
    <time dateTime={date.toISOString()} title={date.toLocaleString()} className="ml-1.5 text-[11px] text-slate-400">
      {formatRelativeTime(date, now)}
    </time>
  );
}

function useNow(intervalMs = 30_000) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(id);
  }, [intervalMs]);
  return now;
}

const PENDING_STEPS = [
  { after: 0, text: "Understanding your question" },
  { after: 2000, text: "Searching your documents" },
  { after: 5000, text: "Reading the most relevant passages" },
  { after: 9000, text: "Checking the sources" },
  { after: 14000, text: "Writing the answer" },
  { after: 25000, text: "Still working on it, longer questions can take a moment" },
];

function PendingAnswer() {
  const [step, setStep] = useState(0);

  useEffect(() => {
    const timers = PENDING_STEPS.slice(1).map((s, i) =>
      window.setTimeout(() => setStep(i + 1), s.after),
    );
    return () => timers.forEach((id) => window.clearTimeout(id));
  }, []);

  return (
    <div className="flex items-start gap-3">
      <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-violet-100 text-violet-700">
        <Icon name="spark" size={18} />
      </div>

      <div role="status" aria-live="polite" className="flex min-w-0 items-center gap-2.5 rounded-2xl border border-slate-200 
        bg-white px-4 py-3 text-sm text-slate-500">
        <span className="min-w-0 break-words">{PENDING_STEPS[step].text}</span>

        <span className="flex shrink-0 items-center gap-1" aria-hidden="true">
          <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-violet-400 motion-reduce:animate-none" />
          <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-violet-400 [animation-delay:150ms] motion-reduce:animate-none" />
          <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-violet-400 [animation-delay:300ms] motion-reduce:animate-none" />
        </span>
      </div>
    </div>
  );
}

export default function ChatPanel({
  documents,
  conversationId, conversationTitle,
  messages, chatInput,
  isCreatingConversation,
  isSendingMessage,
  chatEndRef,
  selectedDocumentIds,
  isLoadingConversation,
  isUploading,
  setChatInput,
  onToggleDocument,
  onStartNewConversation,
  onSendMessage,
  onStopMessage,
  onEditMessage,
  onCopyMessage,
  onRetryMessage,
  onUploadDocument, }
  : ChatPanelProps) {
  const indexedDocuments = documents.filter((document) => document.status === "Indexed");
  const selectedDocuments = indexedDocuments.filter((document) => selectedDocumentIds.includes(document.id));
  const [isDocumentSidebarOpen, setIsDocumentSidebarOpen] = useState(false);
  const [documentSearch, setDocumentSearch] = useState("");

  const filteredDocuments = useMemo(() => {
    const normalizedSearch = documentSearch.trim().toLowerCase();

    if (!normalizedSearch) return indexedDocuments;

    return indexedDocuments.filter((document) =>
      document.name.toLowerCase().includes(normalizedSearch),
    );
  }, [documentSearch, indexedDocuments]);

  const now = useNow();
  const [copiedMessageId, setCopiedMessageId] = useState<string | null>(null);
  const lastMessageId = messages[messages.length - 1]?.id;

  async function handleCopy(message: ChatMessage) {
    const copied = await onCopyMessage(message.content);
    if (!copied) return;
    setCopiedMessageId(message.id);
    window.setTimeout(
      () => setCopiedMessageId((current) => (current === message.id ? null : current)),
      1500,
    );
  }

  useEffect(() => {
    if (!isDocumentSidebarOpen) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setIsDocumentSidebarOpen(false);
      }
    };

    window.addEventListener("keydown", handleKeyDown);

    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isDocumentSidebarOpen]);

  return (
    <>
      {/* Header */}
      <div className="flex items-center gap-2 border-b border-slate-100 bg-white px-4 py-3 sm:px-5 lg:gap-3 lg:px-6">
        <div className="flex min-w-0 flex-1 items-center gap-2">
          <div className="min-w-0">
            <h1 className="truncate text-lg font-bold text-slate-800 sm:text-xl">
              {conversationTitle || "New Conversation"}
            </h1>
            {conversationId && (
              <p className="mt-0.5 truncate text-[11px] text-slate-400">ID: {conversationId}</p>
            )}
          </div>

          <button type="button" onClick={() => setIsDocumentSidebarOpen(true)} aria-haspopup="dialog"
            aria-expanded={isDocumentSidebarOpen} aria-label={
              selectedDocuments.length
                ? `Choose documents (${selectedDocuments.length} selected)`
                : "Choose documents"
            }
            title="Choose documents"
            className="relative flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-slate-200 bg-white text-slate-600 shadow-sm transition hover:border-violet-300 hover:text-violet-700 focus:outline-none focus:ring-4 focus:ring-violet-50"
          >
            <Icon name="file" size={17} />
            {selectedDocuments.length > 0 && (
              <span className="absolute -right-1.5 -top-1.5 flex h-4 min-w-[16px] items-center justify-center rounded-full bg-violet-600 px-1 text-[10px] font-semibold text-white">
                {selectedDocuments.length}
              </span>
            )}
          </button>
        </div>

        {/* Large screens only */}
        {selectedDocuments.length > 0 && (
          <div className="hidden max-w-[45%] flex-wrap justify-end gap-2 lg:flex">
            {selectedDocuments.map((document) => (
              <span
                key={document.id}
                className="inline-flex max-w-full items-center gap-1.5 rounded-full bg-violet-50 px-2.5 py-1 text-xs font-medium text-violet-700"
              >
                <span className="max-w-[200px] truncate">{document.name}</span>
                <button
                  type="button"
                  aria-label={`Remove ${document.name}`}
                  onClick={() => onToggleDocument(document.id, false)}
                  className="text-sm font-semibold leading-none transition hover:text-violet-900"
                >
                  ×
                </button>
              </span>
            ))}
          </div>
        )}
      </div>

      {/* Document selector drawer: sibling of the header, never nested inside it */}
      {isDocumentSidebarOpen && (
        <div className="fixed inset-0 z-50">
          <button type="button" aria-label="Close document selector" onClick={() => setIsDocumentSidebarOpen(false)}
            className="absolute inset-0 h-full w-full cursor-default bg-slate-950/35 backdrop-blur-[1px]"
          />

          <aside role="dialog" aria-modal="true" aria-labelledby="document-sidebar-title"
            className="absolute inset-y-0 right-0 flex w-full max-w-md flex-col border-l border-slate-200 bg-white shadow-2xl">
            <div className="flex items-start justify-between border-b border-slate-100 px-5 py-5">
              <div>
                <h2 id="document-sidebar-title" className="text-lg font-semibold text-slate-900">Choose documents</h2>
                <p className="mt-1 text-sm text-slate-500">Select indexed documents to use in this conversation.</p>
              </div>
              <button
                type="button"
                aria-label="Close document selector"
                onClick={() => setIsDocumentSidebarOpen(false)}
                className="ml-4 flex h-9 w-9 shrink-0 items-center justify-center rounded-lg text-slate-500 transition hover:bg-slate-100 hover:text-slate-800"
              >
                <span aria-hidden="true" className="text-2xl leading-none">×</span>
              </button>
            </div>

            <div className="border-b border-slate-100 p-4">
              <div className="flex items-center gap-2">
                <label className="flex min-w-0 flex-1 items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-slate-400 focus-within:border-violet-300 focus-within:ring-4 focus-within:ring-violet-50">
                  <Icon name="search" size={17} />
                  <input
                    type="search"
                    value={documentSearch}
                    onChange={(event) => setDocumentSearch(event.target.value)}
                    placeholder="Search indexed documents..."
                    aria-label="Search indexed documents"
                    className="min-w-0 flex-1 bg-transparent text-sm text-slate-800 outline-none placeholder:text-slate-400"
                  />
                </label>
                <button
                  type="button"
                  onClick={onUploadDocument}
                  disabled={isUploading}
                  aria-label="Upload a document"
                  title={isUploading ? "Uploading..." : "Upload a PDF"}
                  className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-violet-600 text-white transition hover:bg-violet-700 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  <Icon name="plus" size={20} />
                </button>
              </div>
              <p className="mt-2 text-xs text-slate-500">Only indexed documents appear here. Use + to upload a PDF.</p>

              {selectedDocuments.length > 0 && (
                <div className="mt-3 flex w-full flex-row flex-wrap gap-2">
                  {selectedDocuments.map((document) => (
                    <span
                      key={document.id}
                      title={document.name}
                      className="inline-flex max-w-full items-center gap-1.5 rounded-full bg-violet-50 px-2.5 py-1 text-xs font-medium text-violet-700"
                    >
                      <span className="max-w-[260px] truncate">{document.name}</span>
                      <button
                        type="button"
                        aria-label={`Remove ${document.name}`}
                        onClick={() => onToggleDocument(document.id, false)}
                        className="text-sm font-semibold leading-none transition hover:text-violet-900"
                      >
                        ×
                      </button>
                    </span>
                  ))}
                </div>
              )}
            </div>

            <div className="flex-1 overflow-y-auto p-3">
              {filteredDocuments.length === 0 ? (
                <div className="px-4 py-10 text-center">
                  <div className="mx-auto flex h-11 w-11 items-center justify-center rounded-xl bg-slate-100 text-slate-500">
                    <Icon name="file" size={20} />
                  </div>
                  <p className="mt-3 text-sm font-medium text-slate-700">
                    {indexedDocuments.length === 0 ? "No indexed documents yet" : "No matching documents"}
                  </p>
                  <p className="mt-1 text-xs leading-5 text-slate-500">
                    {indexedDocuments.length === 0 ? "Upload a PDF using the + button above." : "Try a different search term."}
                  </p>
                </div>
              ) : (
                <div role="listbox" aria-label="Indexed documents" aria-multiselectable="true" className="space-y-1">
                  {filteredDocuments.map((document) => {
                    const isSelected = selectedDocumentIds.includes(document.id);
                    return (
                      <button
                        key={document.id}
                        type="button"
                        role="option"
                        aria-selected={isSelected}
                        onClick={() => onToggleDocument(document.id, !isSelected)}
                        title={document.name}
                        className={`flex w-full items-center gap-3 rounded-xl px-3 py-3 text-left transition ${isSelected ? "bg-violet-50 text-violet-800" : "text-slate-700 hover:bg-slate-50"}`}
                      >
                        <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg ${isSelected ? "bg-violet-100 text-violet-700" : "bg-slate-100 text-slate-500"}`}>
                          <Icon name="file" size={17} />
                        </span>
                        <span className="min-w-0 flex-1 break-words text-sm font-medium">{document.name}</span>
                        {isSelected && <Icon name="check" size={18} />}
                      </button>
                    );
                  })}
                </div>
              )}
            </div>

            <div className="border-t border-slate-100 p-4">
              <div className="mb-3 text-xs text-slate-500">
                {selectedDocuments.length} document{selectedDocuments.length === 1 ? "" : "s"} selected
              </div>
              <button
                type="button"
                onClick={() => setIsDocumentSidebarOpen(false)}
                className="w-full rounded-xl bg-violet-600 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-violet-700"
              >
                Done
              </button>
            </div>
          </aside>
        </div>
      )}

      {/* Messages */}
      <div className="flex min-h-[360px] flex-1 flex-col gap-5 p-5 sm:p-6">
        {isLoadingConversation ? (
          <div role="status" aria-live="polite" className="flex flex-1 items-center justify-center gap-3 py-10 text-sm text-slate-500">
            <span className="h-5 w-5 animate-spin rounded-full border-2 border-violet-200 border-t-violet-600" />
            Loading conversation...
          </div>
        ) : messages.length === 0 && !isSendingMessage ? (
          <div className="flex flex-1 flex-col items-center justify-center py-10 text-center">
            <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-violet-100 text-violet-700">
              <Icon name="spark" size={26} />
            </div>

            <h3 className="mt-4 text-lg font-semibold text-slate-900">
              {conversationId ? "What would you like to know?" : "Your documents, ready for questions"}
            </h3>

            <p className="mt-2 max-w-md text-sm leading-6 text-slate-500">
              {conversationId
                ? "Ask a question about your documents. Answers can include source references, page numbers, and headings."
                : "Choose the documents you want this conversation to use, then start it."}
            </p>

            {!conversationId && (
              <button
                type="button"
                onClick={() => void onStartNewConversation()}
                disabled={isCreatingConversation}
                className="mt-5 rounded-xl bg-violet-600 px-5 py-3 text-sm font-semibold text-white transition hover:bg-violet-700 disabled:opacity-60"
              >
                {isCreatingConversation ? "Starting conversation..." : "Start a conversation"}
              </button>
            )}
          </div>
        ) : (
          <div className="flex flex-col gap-5">
            {messages.map((message) => {
              const isUser = message.role === "user";
              const canRetry = message.id === lastMessageId && !isSendingMessage;

              return (
                <div key={message.id} className={`flex gap-3 ${isUser ? "justify-end" : "justify-start"}`}>
                  {!isUser && (
                    <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-violet-100 text-violet-700">
                      <Icon name="spark" size={18} />
                    </div>
                  )}

                  <div className={`flex min-w-0 max-w-[90%] flex-col sm:max-w-[80%] ${isUser ? "items-end" : "items-start"}`}>
                    <div
                      className={`min-w-0 max-w-full rounded-2xl px-4 py-3 ${isUser ? "bg-violet-600 text-white" : "border border-slate-200 bg-white text-slate-800"
                        }`}
                    >
                      <div className="whitespace-pre-wrap break-words text-sm leading-7">
                        {message.content}
                      </div>

                      {!isUser && message.insufficientEvidence && (
                        <div className="mt-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs leading-5 text-amber-800">
                          There was not enough evidence in the retrieved documents
                          to answer this confidently.
                        </div>
                      )}

                      {!isUser && (message.citations?.length ?? 0) > 0 && (
                        <div className="mt-4 border-t border-slate-200 pt-3">
                          <div className="mb-2 flex items-center gap-2 text-xs font-semibold text-slate-600">
                            <Icon name="book" size={15} />
                            Sources
                          </div>

                          <div className="flex flex-col gap-2">
                            {message.citations!.map((citation, index) => (
                              <div
                                key={`${citation.chunkId}-${index}`}
                                className="rounded-xl border border-slate-200 bg-slate-50 p-3"
                              >
                                <div className="flex flex-wrap items-center gap-2">
                                  <span className="rounded-md bg-violet-100 px-2 py-1 text-[11px] font-semibold text-violet-700">
                                    {citation.referenceId || `Source ${index + 1}`}
                                  </span>

                                  <span className="text-xs font-medium text-slate-600">
                                    {citation.startPage === citation.endPage
                                      ? `Page ${citation.startPage}`
                                      : `Pages ${citation.startPage}–${citation.endPage}`}
                                  </span>
                                </div>

                                {citation.headingPath && (
                                  <p className="mt-2 break-words text-xs leading-5 text-slate-600">
                                    {citation.headingPath}
                                  </p>
                                )}

                                {!citation.headingPath && citation.heading && (
                                  <p className="mt-2 break-words text-xs leading-5 text-slate-600">
                                    {citation.heading}
                                  </p>
                                )}
                              </div>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>

                    <div className="mt-1 flex items-center gap-0.5 px-1">
                      <button type="button" onClick={() => void handleCopy(message)}
                        aria-label="Copy message" title="Copy" className={actionButtonClass}>
                        {copiedMessageId === message.id ? <CheckIcon /> : <CopyIcon />}
                      </button>

                      {isUser && (
                        <button type="button" onClick={() => onEditMessage(message)} disabled={isSendingMessage}
                          aria-label="Edit message" title="Edit" className={actionButtonClass}>
                          <EditIcon />
                        </button>
                      )}

                      {canRetry && (
                        <button type="button" onClick={() => onRetryMessage(message)}
                          aria-label="Retry" title="Retry" className={actionButtonClass}>
                          <RetryIcon />
                        </button>
                      )}

                      <MessageTime value={message.createdAt} now={now} />
                    </div>
                  </div>
                </div>
              );
            })}

            {isSendingMessage && <PendingAnswer />}

            <div ref={chatEndRef} />
          </div>
        )}
      </div>

      {/* Input */}
      <form
        onSubmit={onSendMessage}
        className="sticky bottom-0 z-10 border-t border-slate-100 bg-slate-50 p-4 pb-[max(1rem,env(safe-area-inset-bottom))] sm:p-5 sm:pb-[max(1.25rem,env(safe-area-inset-bottom))]"
      >
        <div className="flex items-end gap-3 rounded-xl border border-slate-200 bg-white p-2 shadow-sm focus-within:border-violet-300 focus-within:ring-4 focus-within:ring-violet-50">
          <textarea
            value={chatInput}
            onChange={(event) => setChatInput(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter" && !event.shiftKey) {
                event.preventDefault();
                event.currentTarget.form?.requestSubmit();
              }
            }}
            placeholder={
              conversationId
                ? "Ask a question about your documents..."
                : "Start a conversation first..."
            }
            rows={2}
            disabled={!conversationId || isSendingMessage || isLoadingConversation}
            className="max-h-36 min-h-[48px] flex-1 resize-y bg-transparent px-2 py-2 text-sm text-slate-800 outline-none placeholder:text-slate-400 disabled:cursor-not-allowed disabled:opacity-60"
          />

          {isSendingMessage ? (
            <button type="button" onClick={onStopMessage} aria-label="Stop response" className="flex h-10 shrink-0 items-center justify-center rounded-lg bg-rose-600 px-3 text-sm font-semibold text-white transition hover:bg-rose-700">Stop</button>
          ) : (
            <button type="submit" disabled={!conversationId || !chatInput.trim() || isCreatingConversation} aria-label="Send message" className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-violet-600 text-white transition hover:bg-violet-700 disabled:cursor-not-allowed disabled:opacity-40">
              <Icon name="chevron" size={20} />
            </button>
          )}
        </div>

        <p className="mt-2 px-1 text-[11px] text-slate-400">
          Press Enter to send · Shift + Enter for a new line
        </p>
      </form>
    </>
  );
}
