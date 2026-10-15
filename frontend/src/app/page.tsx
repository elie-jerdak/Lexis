
"use client";

import { useEffect, useMemo, useRef, useState } from "react"; 
import { apiRequest, getAccessToken } from "@/lib/api";
import { createPortal } from "react-dom";

type DocumentStatus =
  | "Pending"
  | "Processing"
  | "Indexed"
  | "Chunked"
  | "Failed";

type DocumentItem = {
  id: string;
  name: string;
  type: string;
  size: string;
  updated: string;
  status: DocumentStatus;
  color: string;
};

type DocumentDto = {
  id: string;
  fileName: string;
  mimeType: string;
  fileSize: number;
  status: string | number;
  uploadedAt: string;
};

const documentColors = ["violet", "blue", "amber", "rose"];

function formatFileSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return "Unknown size";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`;
  }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function formatDate(value: string): string {
  const date = new Date(value);

  if (Number.isNaN(date.getTime())) return "—";

  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    year: "numeric",
  }).format(date);
}

function normalizeStatus(value: string | number): DocumentStatus {
  // Supports numeric enum values if the API serializes enums as numbers.
  if (typeof value === "number") {
    const numericStatuses: Record<number, DocumentStatus> = {
      0: "Pending",
      1: "Processing",
      2: "Indexed",
      3: "Failed",
      4: "Chunked",
    };

    return numericStatuses[value] ?? "Pending";
  }

  const normalized = value.toLowerCase();

  switch (normalized) {
    case "pending":
      return "Pending";
    case "processing":
      return "Processing";
    case "indexed":
      return "Indexed";
    case "chunked":
      return "Chunked";
    case "failed":
      return "Failed";
    default:
      return "Pending";
  }
}

function mapDocument(dto: DocumentDto, index: number): DocumentItem {
  return {
    id: dto.id,
    name: dto.fileName,
    type: dto.mimeType || "Document",
    size: formatFileSize(dto.fileSize),
    updated: formatDate(dto.uploadedAt),
    status: normalizeStatus(dto.status),
    color: documentColors[index % documentColors.length],
  };
}

const navItems = [
  { label: "Overview", icon: "grid" },
  { label: "Documents", icon: "file" },
  { label: "Chat", icon: "message" },
] as const;

type NavItem = (typeof navItems)[number]["label"];

function Icon({
  name,
  size = 18,
}: {
  name: string;
  size?: number;
}) {
  const common = {
    width: size,
    height: size,
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: 1.7,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
    "aria-hidden": true as const,
  };

  switch (name) {
    case "download":
      return (
        <svg {...common}>
          <path d="M12 3v12m-5-5 5 5 5-5" />
          <path d="M5 17v3a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-3" />
        </svg>
      );

    case "trash-2":
      return (
        <svg {...common}>
          <path d="M3 6h18" />
          <path d="M8 6V4h8v2" />
          <path d="m19 6-1 14H6L5 6" />
          <path d="M10 10v6m4-6v6" />
        </svg>
      );

    case "grid":
      return (
        <svg {...common}>
          <rect x="3.5" y="3.5" width="7" height="7" rx="1.5" />
          <rect x="13.5" y="3.5" width="7" height="7" rx="1.5" />
          <rect x="3.5" y="13.5" width="7" height="7" rx="1.5" />
          <rect x="13.5" y="13.5" width="7" height="7" rx="1.5" />
        </svg>
      );
    case "file":
      return (
        <svg {...common}>
          <path d="M13 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V10Z" />
          <path d="M13 3v7h7M8 14h8M8 17.5h8" />
        </svg>
      );
    case "message":
      return (
        <svg {...common}>
          <path d="M20 11.5a7.5 7.5 0 0 1-7.5 7.5H5l-2 2v-7.5a7.5 7.5 0 1 1 17  -2Z" />
          <path d="M8 10h8M8 13.5h5" />
        </svg>
      );
    case "search":
      return (
        <svg {...common}>
          <circle cx="10.8" cy="10.8" r="6.8" />
          <path d="m16 16 4.5 4.5" />
        </svg>
      );
    case "upload":
      return (
        <svg {...common}>
          <path d="M12 16V4m-4 4 4-4 4 4" />
          <path d="M5 14v5a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-5" />
        </svg>
      );
    case "plus":
      return (
        <svg {...common}>
          <path d="M12 5v14M5 12h14" />
        </svg>
      );
    case "chevron":
      return (
        <svg {...common}>
          <path d="m9 18 6-6-6-6" />
        </svg>
      );
    case "more":
      return (
        <svg {...common}>
          <circle cx="5" cy="12" r="1" />
          <circle cx="12" cy="12" r="1" />
          <circle cx="19" cy="12" r="1" />
        </svg>
      );
    case "spark":
      return (
        <svg {...common}>
          <path d="m12 3 1.9 5.8L20 11l-6.1 2.2L12 19l-2-5.8L4 11l6-2.2Z" />
          <path d="m19 14 1.1 2.1L22 17l-1.9.9L19 20l-.9-2.1L16 17l2.1-.9Z" />
        </svg>
      );
    case "clock":
      return (
        <svg {...common}>
          <circle cx="12" cy="12" r="9" />
          <path d="M12 7v5l3 2" />
        </svg>
      );
    case "check":
      return (
        <svg {...common}>
          <path d="m5 12 4 4L19 6" />
        </svg>
      );
    case "alert":
      return (
        <svg {...common}>
          <path d="M12 3 2.8 19h18.4Z" />
          <path d="M12 9v4m0 3h.01" />
        </svg>
      );
    case "book":
      return (
        <svg {...common}>
          <path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H20v17H6.5A2.5 2.5 0 0 0 4 22Z" />
          <path d="M4 5.5V22m4-14h8m-8 4h8" />
        </svg>
      );
    case "settings":
      return (
        <svg {...common}>
          <circle cx="12" cy="12" r="3" />
          <path d="m19.4 15 .1.1 1.4 1.1-1.5 2.6-1.7-.7a8 8 0 0 1-2 .9l-.3 1.8h-3l-.3-1.8a8 8 0 0 1-2-.9l-1.7.7-1.5-2.6 1.4-1.1a7 7 0 0 1 0-2l-1.4-1.1 1.5-2.6 1.7.7a8 8 0 0 1 2-.9l.3-1.8h3l.3 1.8a8 8 0 0 1 2 .9l1.7-.7 1.5 2.6-1.4 1.1a7 7 0 0 1 0 2Z" transform="translate(-1 -1) scale(1.08)" />
        </svg>
      );
    default:
      return null;
  }
}

function StatusBadge({ status }: { status: DocumentStatus }) {
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
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ring-1 ring-inset ${styles[status]}`}
    >
      <span className={`h-1.5 w-1.5 rounded-full ${dotStyles[status]}`} />
      {status}
    </span>
  );
}

export default function Home() {
  const [activeNav, setActiveNav] = useState<NavItem>("Overview");
  const [documents, setDocuments] = useState<DocumentItem[]>([]);
  const [openDocumentMenuId, setOpenDocumentMenuId] =
    useState<string | null>(null);
  const [menuPosition, setMenuPosition] = useState<{
    top: number;
    right: number;
  } | null>(null);
  const [selectedDocument, setSelectedDocument] =
    useState<DocumentItem | null>(null);

  const [isLoadingDetails, setIsLoadingDetails] = useState(false);
  const [isLoadingDocuments, setIsLoadingDocuments] = useState(true);
  const [isUploading, setIsUploading] = useState(false);
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<"All" | DocumentStatus>("All");
  const [notice, setNotice] = useState("");
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    void loadDocuments();
  }, []);

  async function loadDocuments() {
    setIsLoadingDocuments(true);

    try {
      const result = await apiRequest<DocumentDto[]>("/api/Document");
      setDocuments(result.map(mapDocument));
    } catch (error) {
      setNotice(
        error instanceof Error
          ? `Could not load documents: ${error.message}`
          : "Could not load documents.",
      );
    } finally {
      setIsLoadingDocuments(false);
    }
  }

  async function handleViewDetails(document: DocumentItem) {
    setOpenDocumentMenuId(null);
    setMenuPosition(null);
    setIsLoadingDetails(true);

    try {
      const result = await apiRequest<DocumentDto>(
        `/api/Document/${encodeURIComponent(document.id)}`,
      );

      setSelectedDocument(mapDocument(result, 0));
    } catch (error) {
      setNotice(
        error instanceof Error
          ? `Could not load document details: ${error.message}`
          : "Could not load document details.",
      );
    } finally {
      setIsLoadingDetails(false);
    }
  }

  async function handleDownload(document: DocumentItem) {
    try {
      const token = getAccessToken();

      if (!token) {
        setNotice("Your session has expired. Please log in again.");
        return;
      }

      const apiBaseUrl =
        process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5238";

      const response = await fetch(
        `${apiBaseUrl}/api/Document/${encodeURIComponent(document.id)}/download`,
        {
          method: "GET",
          headers: {
            Authorization: `Bearer ${token}`,
          },
        },
      );

      if (!response.ok) {
        let message = `Download failed (${response.status}).`;

        try {
          const body = await response.json();
          message = body.message ?? message;
        } catch {
          // Keep the default error message if the response isn't JSON.
        }

        throw new Error(message);
      }

      const blob = await response.blob();
      const downloadUrl = URL.createObjectURL(blob);
      const link = window.document.createElement("a");

      link.href = downloadUrl;
      link.download = document.name;
      window.document.body.appendChild(link);
      link.click();
      link.remove();

      window.setTimeout(() => {
        URL.revokeObjectURL(downloadUrl);
      }, 1000);

      setOpenDocumentMenuId(null);
      setNotice(`Downloaded ${document.name}.`);
    } catch (error) {
      setNotice(
        error instanceof Error
          ? error.message
          : "Could not download the document.",
      );
    }
  }

  async function handleDelete(document: DocumentItem) {
    const confirmed = window.confirm(
      `Delete "${document.name}"? This action cannot be undone.`,
    );

    if (!confirmed) return;

    try {
      await apiRequest<{ message: string }>(
        `/api/Document/${encodeURIComponent(document.id)}`,
        {
          method: "DELETE",
        },
      );

      setDocuments((current) =>
        current.filter((item) => item.id !== document.id),
      );

      setOpenDocumentMenuId(null);
      setNotice(`Deleted ${document.name}.`);
    } catch (error) {
      setNotice(
        error instanceof Error
          ? `Could not delete ${document.name}: ${error.message}`
          : `Could not delete ${document.name}.`,
      );
    }
  }

  const filteredDocuments = useMemo(() => {
    return documents.filter((document) => {
      const matchesQuery = document.name
        .toLowerCase()
        .includes(query.toLowerCase());
      const matchesFilter = filter === "All" || document.status === filter;
      return matchesQuery && matchesFilter;
    });
  }, [documents, query, filter]);

  const indexedCount = documents.filter(
    (document) => document.status === "Indexed",
  ).length;

  const processingCount = documents.filter(
    (document) => document.status === "Processing",
  ).length;

  const failedCount = documents.filter(
    (document) => document.status === "Failed",
  ).length;

  async function handleFileSelection(
    event: React.ChangeEvent<HTMLInputElement>,
  ) {
    const files = Array.from(event.target.files ?? []);
    event.target.value = "";

    if (files.length === 0) return;

    const invalidFiles = files.filter(
      (file) =>
        file.type !== "application/pdf" &&
        !file.name.toLowerCase().endsWith(".pdf"),
    );

    if (invalidFiles.length > 0) {
      setNotice("Please select PDF files only.");
      return;
    }

    setIsUploading(true);
    setNotice(`Uploading ${files.length} PDF file(s)...`);

    let uploaded = 0;
    const errors: string[] = [];

    try {
      for (const file of files) {
        const formData = new FormData();
        formData.append("file", file);

        try {
          await apiRequest<unknown>("/api/Document", {
            method: "POST",
            body: formData,
          });

          uploaded++;
        } catch (error) {
          errors.push(
            `${file.name}: ${error instanceof Error ? error.message : "Upload failed"
            }`,
          );
        }
      }

      await loadDocuments();

      if (errors.length > 0) {
        setNotice(
          `${uploaded} of ${files.length} file(s) uploaded. ${errors.join(" | ")}`,
        );
      } else {
        setNotice(
          `${uploaded} PDF file(s) uploaded successfully.`,
        );
      }
    } finally {
      setIsUploading(false);
    }
  }

  function openUploadPicker() {
    fileInputRef.current?.click();
  }

  const pageTitle =
    activeNav === "Overview"
      ? "Overview"
      : activeNav === "Documents"
        ? "Your documents"
        : "Chat";

  return (
    <>
      <div className="min-h-screen bg-[#f8f9fc] text-slate-900">
        <input
          ref={fileInputRef}
          type="file"
          accept=".pdf,application/pdf"
          multiple
          className="hidden"
          onChange={handleFileSelection}
        />

        <div className="flex min-h-screen">
          <aside className="hidden w-[248px] shrink-0 flex-col border-r border-slate-200/80 bg-white lg:flex">
            <div className="flex h-[76px] items-center gap-3 border-b border-slate-100 px-6">
              <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-violet-600 text-white shadow-sm shadow-violet-200">
                <Icon name="book" size={21} />
              </div>
              <div>
                <div className="text-[19px] font-semibold tracking-tight text-slate-900">
                  Lexis
                </div>
                <div className="text-[10px] font-medium uppercase tracking-[0.19em] text-slate-400">
                  Knowledge workspace
                </div>
              </div>
            </div>

            <div className="px-4 pt-7">
              <button
                onClick={() => {
                  setActiveNav("Documents");
                  openUploadPicker();
                }}
                className="flex w-full items-center justify-center gap-2 rounded-xl bg-violet-600 px-4 py-3 text-sm font-semibold text-white shadow-sm transition hover:bg-violet-700 focus:outline-none focus:ring-4 focus:ring-violet-100"
              >
                <Icon name="plus" size={18} />
                Add documents
              </button>
            </div>

            <nav className="mt-8 space-y-1 px-3">
              <p className="mb-3 px-3 text-[10px] font-semibold uppercase tracking-[0.17em] text-slate-400">
                Workspace
              </p>
              {navItems.map((item) => {
                const active = activeNav === item.label;

                return (
                  <button
                    key={item.label}
                    onClick={() => setActiveNav(item.label)}
                    className={`flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-sm transition ${active
                      ? "bg-violet-50 font-semibold text-violet-700"
                      : "font-medium text-slate-500 hover:bg-slate-50 hover:text-slate-800"
                      }`}
                  >
                    <Icon name={item.icon} size={18} />
                    {item.label}
                    {item.label === "Documents" && (
                      <span className="ml-auto rounded-md bg-slate-100 px-2 py-0.5 text-xs text-slate-500">
                        {documents.length}
                      </span>
                    )}
                  </button>
                );
              })}
            </nav>

            <div className="mt-9 px-6">
              <p className="mb-4 text-[10px] font-semibold uppercase tracking-[0.17em] text-slate-400">
                Library
              </p>
              <div className="space-y-4">
                <div className="flex items-center gap-3 text-sm text-slate-500">
                  <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-blue-50 text-blue-600">
                    <Icon name="file" size={16} />
                  </span>
                  All documents
                </div>
                <div className="flex items-center gap-3 text-sm text-slate-500">
                  <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-emerald-50 text-emerald-600">
                    <Icon name="check" size={16} />
                  </span>
                  Indexed documents
                </div>
              </div>
            </div>

            <div className="mt-auto p-4">
              <div className="rounded-xl border border-slate-200 bg-slate-50/80 p-4">
                <div className="mb-2 flex items-center gap-2 text-sm font-semibold text-slate-800">
                  <Icon name="spark" size={17} />
                  Your knowledge base
                </div>
                <p className="text-xs leading-5 text-slate-500">
                  Upload documents to build a searchable knowledge workspace.
                </p>
                <div className="mt-4 h-1.5 overflow-hidden rounded-full bg-slate-200">
                  <div
                    className="h-full rounded-full bg-violet-500"
                    style={{
                      width: `${documents.length ? (indexedCount / documents.length) * 100 : 0}%`,
                    }}
                  />
                </div>
                <div className="mt-2 flex justify-between text-[11px] text-slate-400">
                  <span>Indexed</span>
                  <span>
                    {indexedCount} of {documents.length}
                  </span>
                </div>
              </div>
              <button
                onClick={() =>
                  setNotice("Settings will be connected in a later step.")
                }
                className="mt-3 flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-sm text-slate-500 transition hover:bg-slate-100 hover:text-slate-800"
              >
                <Icon name="settings" size={18} />
                Settings
              </button>
              <div className="mt-3 flex items-center gap-3 border-t border-slate-100 px-2 pt-4">
                <div className="flex h-9 w-9 items-center justify-center rounded-full bg-slate-200 text-xs font-bold text-slate-600">
                  U
                </div>
                <div className="min-w-0 flex-1">
                  <div className="truncate text-sm font-semibold text-slate-800">
                    Your workspace
                  </div>
                  <div className="text-xs text-slate-400">Personal account</div>
                </div>
                <button
                  aria-label="Account options"
                  onClick={() =>
                    setNotice("Account options will be connected later.")
                  }
                  className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100"
                >
                  <Icon name="more" size={18} />
                </button>
              </div>
            </div>
          </aside>

          <main className="min-w-0 flex-1">
            <header className="sticky top-0 z-10 flex h-[76px] items-center justify-between border-b border-slate-200/80 bg-white/90 px-5 backdrop-blur-md sm:px-8 lg:px-10">
              <div className="flex min-w-0 items-center gap-3">
                <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-violet-600 text-white lg:hidden">
                  <Icon name="book" size={20} />
                </div>
                <div>
                  <div className="text-xs text-slate-400">Workspace /</div>
                  <h1 className="text-base font-semibold tracking-tight text-slate-900">
                    {pageTitle}
                  </h1>
                </div>
              </div>
              <div className="flex items-center gap-3">
                <div className="hidden items-center gap-2 rounded-full border border-amber-200 bg-amber-50 px-3 py-1.5 text-xs font-medium text-amber-800 sm:flex">
                  <span className="h-1.5 w-1.5 rounded-full bg-amber-500" />
                  Live data
                </div>
                <button
                  onClick={openUploadPicker}
                  disabled={isUploading}
                  className="flex items-center gap-2 rounded-lg bg-violet-600 px-3.5 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-violet-700 disabled:cursor-not-allowed disabled:opacity-60 sm:px-4"
                >
                  <Icon name="upload" size={17} />
                  <span className="hidden sm:inline">
                    {isUploading ? "Uploading..." : "Upload PDF"}
                  </span>
                  <span className="sm:hidden">
                    {isUploading ? "Uploading..." : "Upload"}
                  </span>
                </button>
              </div>
            </header>

            <div className="mx-auto max-w-[1440px] px-5 py-7 sm:px-8 sm:py-9 lg:px-10">
              {notice && (
                <div
                  role="status"
                  className="mb-5 flex items-start justify-between gap-4 rounded-xl border border-violet-200 bg-violet-50 px-4 py-3 text-sm text-violet-800"
                >
                  <span>{notice}</span>
                  <button
                    aria-label="Dismiss notification"
                    onClick={() => setNotice("")}
                    className="shrink-0 text-violet-500 hover:text-violet-800"
                  >
                    ×
                  </button>
                </div>
              )}

              {activeNav === "Chat" ? (
                <section className="flex min-h-[65vh] flex-col items-center justify-center text-center">
                  <div className="mb-5 flex h-16 w-16 items-center justify-center rounded-2xl bg-violet-100 text-violet-700">
                    <Icon name="message" size={30} />
                  </div>
                  <h2 className="text-2xl font-semibold tracking-tight">
                    Chat with your documents
                  </h2>
                  <p className="mt-2 max-w-md text-sm leading-6 text-slate-500">
                    The chat interface is our next milestone. We&apos;ll connect
                    it to your existing RAG endpoints and citation data.
                  </p>
                  <button
                    onClick={() => setActiveNav("Documents")}
                    className="mt-6 rounded-xl bg-violet-600 px-5 py-3 text-sm font-semibold text-white hover:bg-violet-700"
                  >
                    Browse documents
                  </button>
                </section>
              ) : (
                <>
                  <div className="mb-8 flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
                    <div>
                      <div className="mb-2 flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.16em] text-violet-600">
                        <span className="h-1.5 w-1.5 rounded-full bg-violet-500" />
                        Your knowledge, organized
                      </div>
                      <h2 className="text-[28px] font-semibold tracking-tight text-slate-900 sm:text-[32px]">
                        {activeNav === "Documents"
                          ? "Your documents"
                          : "Good to see you"}
                      </h2>
                      <p className="mt-2 max-w-xl text-sm leading-6 text-slate-500">
                        Manage your documents and turn your knowledge into
                        answers you can trust.
                      </p>
                    </div>
                    <button
                      onClick={() => setActiveNav("Chat")}
                      className="inline-flex w-fit items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-sm font-semibold text-slate-700 shadow-sm transition hover:border-violet-200 hover:text-violet-700"
                    >
                      <Icon name="spark" size={17} />
                      Open document chat
                      <Icon name="chevron" size={15} />
                    </button>
                  </div>

                  <section className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                    <div className="rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm shadow-slate-200/30">
                      <div className="flex items-start justify-between">
                        <span className="text-sm font-medium text-slate-500">
                          Total documents
                        </span>
                        <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-violet-50 text-violet-600">
                          <Icon name="file" size={19} />
                        </span>
                      </div>
                      <div className="mt-4 text-3xl font-semibold tracking-tight">
                        {documents.length}
                      </div>
                      <p className="mt-1 text-xs text-slate-400">
                        In your workspace
                      </p>
                    </div>

                    <div className="rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm shadow-slate-200/30">
                      <div className="flex items-start justify-between">
                        <span className="text-sm font-medium text-slate-500">
                          Indexed
                        </span>
                        <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-emerald-50 text-emerald-600">
                          <Icon name="check" size={19} />
                        </span>
                      </div>
                      <div className="mt-4 text-3xl font-semibold tracking-tight">
                        {indexedCount}
                      </div>
                      <p className="mt-1 text-xs text-slate-400">
                        Ready to query
                      </p>
                    </div>

                    <div className="rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm shadow-slate-200/30">
                      <div className="flex items-start justify-between">
                        <span className="text-sm font-medium text-slate-500">
                          Processing
                        </span>
                        <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-amber-50 text-amber-600">
                          <Icon name="clock" size={19} />
                        </span>
                      </div>
                      <div className="mt-4 text-3xl font-semibold tracking-tight">
                        {processingCount}
                      </div>
                      <p className="mt-1 text-xs text-slate-400">
                        Being prepared
                      </p>
                    </div>

                    <div className="rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm shadow-slate-200/30">
                      <div className="flex items-start justify-between">
                        <span className="text-sm font-medium text-slate-500">
                          Needs attention
                        </span>
                        <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-rose-50 text-rose-600">
                          <Icon name="alert" size={19} />
                        </span>
                      </div>
                      <div className="mt-4 text-3xl font-semibold tracking-tight">
                        {failedCount}
                      </div>
                      <p className="mt-1 text-xs text-slate-400">
                        Failed processing
                      </p>
                    </div>
                  </section>

                  <section className="mt-8 overflow-hidden rounded-2xl border border-slate-200/80 bg-white shadow-sm shadow-slate-200/30">
                    <div className="flex flex-col gap-4 border-b border-slate-100 p-5 sm:flex-row sm:items-center sm:justify-between sm:px-6">
                      <div>
                        <h3 className="text-base font-semibold text-slate-900">
                          Recent documents
                        </h3>
                        <p className="mt-1 text-xs text-slate-400">
                          Your document library and processing status
                        </p>
                      </div>
                      <button
                        onClick={openUploadPicker}
                        className="inline-flex items-center justify-center gap-2 rounded-lg border border-slate-200 px-3.5 py-2 text-sm font-medium text-slate-600 transition hover:border-violet-200 hover:bg-violet-50 hover:text-violet-700"
                      >
                        <Icon name="plus" size={16} />
                        Add document
                      </button>
                    </div>

                    <div className="flex flex-col gap-3 border-b border-slate-100 px-5 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-6">
                      <div className="relative w-full sm:max-w-[300px]">
                        <span className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-400">
                          <Icon name="search" size={17} />
                        </span>
                        <input
                          value={query}
                          onChange={(event) => setQuery(event.target.value)}
                          placeholder="Search documents..."
                          className="w-full rounded-lg border border-slate-200 bg-slate-50/70 py-2.5 pl-10 pr-3 text-sm outline-none transition placeholder:text-slate-400 focus:border-violet-300 focus:bg-white focus:ring-4 focus:ring-violet-50"
                        />
                      </div>
                      <div className="flex flex-wrap items-center gap-1.5">
                        {(
                          ["All", "Pending", "Processing", "Chunked", "Indexed", "Failed"] as const
                        ).map((item) => (
                          <button
                            key={item}
                            onClick={() => setFilter(item)}
                            className={`rounded-lg px-3 py-2 text-xs font-medium transition ${filter === item
                              ? "bg-violet-50 text-violet-700"
                              : "text-slate-500 hover:bg-slate-50 hover:text-slate-800"
                              }`}
                          >
                            {item}
                          </button>
                        ))}
                      </div>
                    </div>

                    <div className="overflow-x-auto overflow-y-visible">
                      <table className="w-full min-w-[680px] text-left">
                        <thead>
                          <tr className="bg-slate-50/70 text-[10px] font-semibold uppercase tracking-[0.13em] text-slate-400">
                            <th className="px-6 py-3.5">Document</th>
                            <th className="px-4 py-3.5">Status</th>
                            <th className="px-4 py-3.5">File size</th>
                            <th className="px-4 py-3.5">Last updated</th>
                            <th className="px-5 py-3.5 text-right"> </th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-100">
                          {isLoadingDocuments && (
                            <tr>
                              <td colSpan={5} className="px-6 py-14 text-center text-sm text-slate-500">
                                Loading your documents...
                              </td>
                            </tr>
                          )}
                          {filteredDocuments.map((document) => (
                            <tr
                              key={document.id}
                              className="transition hover:bg-slate-50/70"
                            >
                              <td className="px-6 py-4">
                                <div className="flex items-center gap-3">
                                  <div
                                    className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${document.color === "violet"
                                      ? "bg-violet-50 text-violet-600"
                                      : document.color === "blue"
                                        ? "bg-blue-50 text-blue-600"
                                        : document.color === "amber"
                                          ? "bg-amber-50 text-amber-600"
                                          : "bg-rose-50 text-rose-600"
                                      }`}
                                  >
                                    <Icon name="file" size={20} />
                                  </div>
                                  <div>
                                    <div className="text-sm font-semibold text-slate-800">
                                      {document.name}
                                    </div>
                                    <div className="mt-1 text-xs text-slate-400">
                                      {document.type}
                                    </div>
                                  </div>
                                </div>
                              </td>
                              <td className="px-4 py-4">
                                <StatusBadge status={document.status} />
                              </td>
                              <td className="px-4 py-4 text-sm text-slate-500">
                                {document.size}
                              </td>
                              <td className="px-4 py-4 text-sm text-slate-500">
                                {document.updated}
                              </td>
                              <td className="px-5 py-4 text-right">


                                <div className="relative inline-flex">
                                  <button
                                    type="button"
                                    onClick={(event) => {
                                      if (openDocumentMenuId === document.id) {
                                        setOpenDocumentMenuId(null);
                                        setMenuPosition(null);
                                        return;
                                      }

                                      const rect = event.currentTarget.getBoundingClientRect();

                                      setMenuPosition({
                                        top: rect.bottom + 4,
                                        right: window.innerWidth - rect.right,
                                      });
                                      setOpenDocumentMenuId(document.id);
                                    }}
                                    aria-label={`Actions for ${document.name}`}
                                    aria-expanded={openDocumentMenuId === document.id}
                                    className="rounded-lg p-2 text-slate-500 transition hover:bg-slate-100 hover:text-slate-800"
                                  >
                                    <Icon name="more" size={18} />
                                  </button>
                                </div>

                                {openDocumentMenuId === document.id &&
                                  menuPosition &&
                                  createPortal(
                                    <div
                                      className="fixed z-[9999] w-44 rounded-xl border border-slate-200 bg-white p-1 text-left shadow-xl"
                                      style={{
                                        top: menuPosition.top,
                                        right: menuPosition.right,
                                      }}
                                    >
                                      <button
                                        type="button"
                                        onClick={() => void handleViewDetails(document)}
                                        disabled={isLoadingDetails}
                                        className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                                      >
                                        <Icon name="file" size={16} />
                                        View details
                                      </button>

                                      <button
                                        type="button"
                                        onClick={() => void handleDownload(document)}
                                        className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-sm text-slate-700 hover:bg-slate-50"
                                      >
                                        <Icon name="download" size={16} />
                                        Download
                                      </button>

                                      <button
                                        type="button"
                                        onClick={() => void handleDelete(document)}
                                        className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-sm text-rose-600 hover:bg-rose-50"
                                      >
                                        <Icon name="trash-2" size={16} />
                                        Delete
                                      </button>
                                    </div>,
                                    window.document.body
                                  )}
                              </td>
                            </tr>
                          ))}
                          {!isLoadingDocuments && filteredDocuments.length === 0 && (
                            <tr>
                              <td
                                colSpan={5}
                                className="px-6 py-14 text-center"
                              >
                                <div className="mx-auto flex h-11 w-11 items-center justify-center rounded-xl bg-slate-100 text-slate-400">
                                  <Icon name="search" size={21} />
                                </div>
                                <p className="mt-3 text-sm font-medium text-slate-700">
                                  No documents found
                                </p>
                                <p className="mt-1 text-xs text-slate-400">
                                  Try another search or change the status filter.
                                </p>
                              </td>
                            </tr>
                          )}
                        </tbody>
                      </table>
                    </div>

                    <div className="flex items-center justify-between border-t border-slate-100 px-5 py-4 text-xs text-slate-400 sm:px-6">
                      <span>
                        Showing {filteredDocuments.length} of {documents.length}{" "}
                        documents
                      </span>
                      <span className="hidden sm:inline">
                        Lexis · Knowledge workspace
                      </span>
                    </div>
                  </section>

                  <section className="mt-6 overflow-hidden rounded-2xl bg-[#17152b] p-6 text-white sm:p-8">
                    <div className="flex flex-col items-start justify-between gap-5 sm:flex-row sm:items-center">
                      <div className="max-w-xl">
                        <div className="mb-3 flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.16em] text-violet-300">
                          <Icon name="spark" size={16} />
                          Ask your knowledge base
                        </div>
                        <h3 className="text-xl font-semibold tracking-tight sm:text-2xl">
                          Your documents. Your answers.
                        </h3>
                        <p className="mt-2 text-sm leading-6 text-slate-300">
                          Ask questions and explore answers grounded in your
                          documents, with sources you can trace back to the
                          original content.
                        </p>
                      </div>
                      <button
                        onClick={() => setActiveNav("Chat")}
                        className="inline-flex shrink-0 items-center gap-2 rounded-xl bg-white px-4 py-3 text-sm font-semibold text-slate-900 transition hover:bg-violet-50"
                      >
                        Start a conversation
                        <Icon name="chevron" size={16} />
                      </button>
                    </div>
                  </section>

                  <footer className="py-6 text-center text-xs text-slate-400">
                    Lexis · Built for knowledge you can trust
                  </footer>
                </>
              )}
            </div>
          </main>
        </div>
      </div>

      {selectedDocument &&
        createPortal(
          <div
            className="fixed inset-0 z-[10000] flex items-center justify-center bg-slate-950/40 p-4"
            onClick={() => setSelectedDocument(null)}
          >
            <div
              role="dialog"
              aria-modal="true"
              aria-labelledby="document-details-title"
              className="w-full max-w-lg rounded-2xl bg-white p-6 shadow-2xl"
              onClick={(event) => event.stopPropagation()}
            >
              <div className="flex items-start justify-between gap-4">
                <div className="flex items-center gap-3">
                  <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-violet-50 text-violet-600">
                    <Icon name="file" size={22} />
                  </div>

                  <div className="min-w-0">
                    <h2
                      id="document-details-title"
                      className="text-lg font-semibold text-slate-900"
                    >
                      Document details
                    </h2>
                    <p className="mt-1 break-all text-sm text-slate-500">
                      {selectedDocument.name}
                    </p>
                  </div>
                </div>

                <button
                  type="button"
                  onClick={() => setSelectedDocument(null)}
                  aria-label="Close document details"
                  className="rounded-lg px-2 py-1 text-xl text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                >
                  ×
                </button>
              </div>

              <div className="mt-6 divide-y divide-slate-100">
                <div className="flex items-center justify-between gap-4 py-3">
                  <span className="text-sm text-slate-500">Status</span>
                  <StatusBadge status={selectedDocument.status} />
                </div>

                <div className="flex items-center justify-between gap-4 py-3">
                  <span className="text-sm text-slate-500">File type</span>
                  <span className="text-right text-sm font-medium text-slate-800">
                    {selectedDocument.type}
                  </span>
                </div>

                <div className="flex items-center justify-between gap-4 py-3">
                  <span className="text-sm text-slate-500">File size</span>
                  <span className="text-sm font-medium text-slate-800">
                    {selectedDocument.size}
                  </span>
                </div>

                <div className="flex items-center justify-between gap-4 py-3">
                  <span className="text-sm text-slate-500">Uploaded</span>
                  <span className="text-right text-sm font-medium text-slate-800">
                    {selectedDocument.updated}
                  </span>
                </div>

                <div className="flex items-center justify-between gap-4 py-3">
                  <span className="text-sm text-slate-500">Document ID</span>
                  <span className="max-w-[65%] break-all text-right font-mono text-xs text-slate-600">
                    {selectedDocument.id}
                  </span>
                </div>
              </div>

              <div className="mt-6 flex justify-end">
                <button
                  type="button"
                  onClick={() => {
                    setSelectedDocument(null);
                    void handleDownload(selectedDocument);
                  }}
                  className="flex items-center gap-2 rounded-lg bg-violet-600 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-violet-700"
                >
                  <Icon name="download" size={16} />
                  Download PDF
                </button>
              </div>
            </div>
          </div>,
          window.document.body
        )}
    </>
  );
}
