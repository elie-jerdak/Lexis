
"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { apiRequest, getAccessToken } from "@/lib/api";
import { createPortal } from "react-dom";
import { Icon } from "@/components/ui/Icon";
import ChatPanel from "@/components/chat/ChatPanel";
import { navItems, type ChatMessage, type ConversationDto, type DocumentDto, type DocumentItem, type DocumentStatus, type NavItem, type RagQueryResponse } from "@/types";
import { mapDocument } from "@/lib/document-utils";
import { SidebarToggleButton } from "@/components/ui/SidebarToggleButton";

export default function Home() {
  const [activeNav, setActiveNav] = useState<NavItem>("Overview");
  const [documents, setDocuments] = useState<DocumentItem[]>([]);
  const [openDocumentMenuId, setOpenDocumentMenuId] = useState<string | null>(null);
  const [menuPosition, setMenuPosition] = useState<{
    top: number;
    right: number;
  } | null>(null);
  const [selectedDocument, setSelectedDocument] = useState<DocumentItem | null>(null);

  const [isLoadingDetails, setIsLoadingDetails] = useState(false);
  const [isLoadingDocuments, setIsLoadingDocuments] = useState(true);
  const [isUploading, setIsUploading] = useState(false);
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<"All" | DocumentStatus>("All");
  const [notice, setNotice] = useState("");
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [conversationId, setConversationId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [chatInput, setChatInput] = useState("");
  const [isCreatingConversation, setIsCreatingConversation] = useState(false);
  const [isSendingMessage, setIsSendingMessage] = useState(false);
  const chatEndRef = useRef<HTMLDivElement>(null);

  const [conversationTitle, setConversationTitle] = useState("");
  const [selectedDocumentIds, setSelectedDocumentIds] = useState<string[]>([]);
  const abortControllerRef = useRef<AbortController | null>(null);

  const [conversations, setConversations] = useState<ConversationDto[]>([]);
  const [isLoadingConversations, setIsLoadingConversations] = useState(true);
  const [isLoadingConversation, setIsLoadingConversation] = useState(false);
  const activeConversationRequestRef = useRef<string | null>(null);
  const [editingConversationId, setEditingConversationId] = useState<string | null>(null);
  const [editingTitle, setEditingTitle] = useState("");


  const [isMobileSidebarOpen, setIsMobileSidebarOpen] = useState(false);
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(false);

  // One icon, two behaviours: drawer on small screens, collapse on desktop.
  function toggleSidebar() {
    if (window.matchMedia("(min-width: 1024px)").matches) {
      setIsSidebarCollapsed((current) => !current);
    } else {
      setIsMobileSidebarOpen((current) => !current);
    }
  }

  useEffect(() => {
    if (!isMobileSidebarOpen) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setIsMobileSidebarOpen(false);
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [isMobileSidebarOpen]);

  useEffect(() => { chatEndRef.current?.scrollIntoView({ behavior: "smooth" }); }, [messages, isSendingMessage]);

  useEffect(() => { void loadDocuments(); void loadConversations(); }, []);

  async function loadConversations() {
    try {
      // ASSUMPTION: GET /api/Conversations returns ConversationDto[] (newest first)
      const result = await apiRequest<ConversationDto[]>("/api/Conversations");
      setConversations(result);
    } catch (error) {
      setNotice(
        error instanceof Error
          ? `Could not load conversations: ${error.message}`
          : "Could not load conversations.",
      );
    } finally {
      setIsLoadingConversations(false);
    }
  }

  async function handleDeleteConversation(conversation: ConversationDto) {
    const confirmed = window.confirm(
      `Delete "${conversation.title || "Untitled conversation"}"? This action cannot be undone.`,
    );
    if (!confirmed) return;

    try {
      await apiRequest<{ message: string }>(
        `/api/Conversations/${encodeURIComponent(conversation.id)}`,
        { method: "DELETE" },
      );

      setConversations((current) => current.filter((c) => c.id !== conversation.id));
      if (conversation.id === conversationId) openNewConversationDraft();
      setNotice("Conversation deleted.");
    } catch (error) {
      setNotice(
        error instanceof Error
          ? `Could not delete the conversation: ${error.message}`
          : "Could not delete the conversation.",
      );
    }
  }

  function startRenameConversation(conversation: ConversationDto) {
    setEditingConversationId(conversation.id);
    setEditingTitle(conversation.title || "");
  }

  async function commitRenameConversation(conversation: ConversationDto) {
    const newTitle = editingTitle.trim();
    setEditingConversationId(null);
    if (!newTitle || newTitle === conversation.title) return;

    const previousTitle = conversation.title;

    // Optimistic update
    setConversations((current) =>
      current.map((c) => (c.id === conversation.id ? { ...c, title: newTitle } : c)),
    );
    if (conversation.id === conversationId) setConversationTitle(newTitle);

    try {
      await apiRequest<ConversationDto>(
        `/api/Conversations/${encodeURIComponent(conversation.id)}`,
        { method: "PATCH", body: JSON.stringify({ title: newTitle }) },
      );
    } catch (error) {
      // Roll back if the server rejected it
      setConversations((current) =>
        current.map((c) => (c.id === conversation.id ? { ...c, title: previousTitle } : c)),
      );
      if (conversation.id === conversationId) setConversationTitle(previousTitle);
      setNotice(
        error instanceof Error
          ? `Could not rename the conversation: ${error.message}`
          : "Could not rename the conversation.",
      );
    }
  }

  async function selectConversation(conversation: ConversationDto) {
    setActiveNav("Chat");
    setIsMobileSidebarOpen(false); 
    if (conversation.id === conversationId) return;

    abortControllerRef.current?.abort(); // don't let an in-flight answer land in the wrong chat
    activeConversationRequestRef.current = conversation.id;

    setConversationId(conversation.id);
    setConversationTitle(conversation.title);
    setMessages([]);
    setChatInput("");
    setNotice("");
    setIsLoadingConversation(true);

    try {
      const id = encodeURIComponent(conversation.id); 
      const [history, docs] = await Promise.all([
        apiRequest<Array<{ id: string; role: "user" | "assistant"; content: string; createdAt?: string; citations?: ChatMessage["citations"]; insufficientEvidence?: boolean }>>(`/api/Conversations/${id}/messages`),
        apiRequest<Array<{ id: string }>>(`/api/Conversations/${id}/documents`),
      ]);

      if (activeConversationRequestRef.current !== conversation.id) return; // user already clicked elsewhere

      console.table(history.map((m) => ({ id: m.id, role: m.role, len: m.content?.length ?? 0 })));

      setMessages(
        history.map((m, i) => ({
          id: `${m.id ?? "msg"}-${i}`,                                    // always-unique React key
          role: String(m.role).toLowerCase() === "user" ? "user" : "assistant",
          content: m.content ?? "",
          createdAt: m.createdAt,
          citations: m.citations ?? [],
          insufficientEvidence: m.insufficientEvidence,
        })),
      );
      
      setSelectedDocumentIds(docs.map((d) => d.id));
    } catch (error) {
      if (activeConversationRequestRef.current !== conversation.id) return;
      setNotice(
        error instanceof Error
          ? `Could not load this conversation: ${error.message}`
          : "Could not load this conversation.",
      );
    } finally {
      if (activeConversationRequestRef.current === conversation.id) setIsLoadingConversation(false);
    }
  }

  // The sidebar "+" resets to the empty state where ChatPanel lets you pick documents and start.
  function openNewConversationDraft() {
    abortControllerRef.current?.abort();
    activeConversationRequestRef.current = null;
    setConversationId(null);
    setConversationTitle("");
    setMessages([]);
    setChatInput("");
    setSelectedDocumentIds([]);
    setNotice("");
    setActiveNav("Chat");
    setIsMobileSidebarOpen(false); 
  }

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

      const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5238";

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

  async function startNewConversation() {
    if (isCreatingConversation || isSendingMessage) return;

    const indexedDocuments = documents.filter(
      (document) => document.status === "Indexed" && selectedDocumentIds.includes(document.id),
    );

    if (selectedDocumentIds.length === 0) {
      setNotice("Select at least one indexed document for this conversation.");
      return;
    }
    if (indexedDocuments.length !== selectedDocumentIds.length) {
      setNotice("Some selected documents are no longer indexed. Refresh your selection and try again.");
      return;
    }

    setIsCreatingConversation(true);
    setNotice("");

    try {
      const conversation = await apiRequest<ConversationDto>(
        "/api/Conversations",
        {
          method: "POST",
          body: JSON.stringify({
            title: "New conversation",
          }),
        },
      );

      if (!conversation?.id) {
        throw new Error(
          "The API created a conversation but did not return its ID.",
        );
      }

      try {
        await apiRequest<unknown>(
          `/api/Conversations/${encodeURIComponent(conversation.id)}/documents`,
          {
            method: "PUT",
            body: JSON.stringify({
              documentIds: indexedDocuments.map((document) => document.id),
            }),
          },
        );
      } catch (error) {
        setNotice(
          error instanceof Error
            ? `Conversation created, but its documents could not be configured: ${error.message}`
            : "Conversation created, but its documents could not be configured.",
        );
        return;
      }

      setConversationId(conversation.id);
      setConversations((current) => [conversation, ...current]);
      setConversationTitle(conversation.title);
      setMessages([]);
      setChatInput("");

      setNotice(
        `New conversation ready with ${indexedDocuments.length} indexed document(s).`,
      );
    } catch (error) {
      setNotice(
        error instanceof Error
          ? `Could not start a conversation: ${error.message}`
          : "Could not start a conversation.",
      );
    } finally {
      setIsCreatingConversation(false);
    }
  }

  async function handleSendMessage(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const question = chatInput.trim();
    if (!question || isSendingMessage) return;

    if (!conversationId) {
      setNotice("Start a new conversation before asking a question.");
      return;
    }

    setChatInput("");
    await askQuestion(question, true);
  }

  async function askQuestion(question: string, appendUserMessage: boolean) {
    if (!conversationId) return;

    if (appendUserMessage) {
      const userMessage: ChatMessage = {
        id: crypto.randomUUID(),
        role: "user",
        content: question,
        createdAt: new Date().toISOString(),
      };
      setMessages((current) => [...current, userMessage]);
    }

    setIsSendingMessage(true);
    setNotice("");
    const abortController = new AbortController();
    abortControllerRef.current = abortController;

    try {
      const result = await apiRequest<RagQueryResponse>("/api/Rag/query", {
        method: "POST",
        signal: abortController.signal,
        body: JSON.stringify({ conversationId, question, limit: 5 }),
      });

      const assistantMessage: ChatMessage = {
        id: crypto.randomUUID(),
        role: "assistant",
        content: result.answer,
        citations: result.citations ?? [],
        insufficientEvidence: result.insufficientEvidence,
        createdAt: new Date().toISOString(),
      };

      setMessages((current) => [...current, assistantMessage]);   // this is the line that was missing

      setConversations((current) => {
        const active = current.find((c) => c.id === conversationId);
        return active ? [active, ...current.filter((c) => c.id !== conversationId)] : current;
      });

      if (!conversationTitle || conversationTitle === "New conversation") {
        const newTitle = question.length > 48 ? `${question.slice(0, 48).trimEnd()}…` : question;
        const targetId = conversationId;

        setConversationTitle(newTitle);
        setConversations((current) =>
          current.map((c) => (c.id === targetId ? { ...c, title: newTitle } : c)),
        );

        apiRequest<ConversationDto>(`/api/Conversations/${encodeURIComponent(targetId)}`, {
          method: "PATCH",
          body: JSON.stringify({ title: newTitle }),
        }).catch(() => setNotice("The conversation title could not be saved."));
      }
    } catch (error) {
      if (error instanceof Error && error.name === "AbortError") {
        setNotice("Response stopped.");
      } else {
        setNotice(
          error instanceof Error
            ? `Could not get an answer: ${error.message}`
            : "Could not get an answer.",
        );
      }
    } finally {
      if (abortControllerRef.current === abortController) abortControllerRef.current = null;
      setIsSendingMessage(false);
    }
  }

  function retryMessage(message: ChatMessage) {
    if (isSendingMessage || !conversationId) return;

    const index = messages.findIndex((m) => m.id === message.id);
    if (index < 0) return;

    // Find the question that produced this message (itself, if it is the question).
    let userIndex = index;
    while (userIndex >= 0 && messages[userIndex].role !== "user") userIndex--;
    if (userIndex < 0) return;

    setMessages(messages.slice(0, userIndex + 1));   // keep the question, drop its answer
    void askQuestion(messages[userIndex].content, false);
  }

  function stopMessage() {
    abortControllerRef.current?.abort();
  }

  function editMessage(message: ChatMessage) {
    if (message.role !== "user" || isSendingMessage) return;
    setChatInput(message.content);
    setMessages((current) => {
      const index = current.findIndex((item) => item.id === message.id);
      return index < 0 ? current : current.slice(0, index);
    });
    setNotice("Question loaded into the composer. Edit it and send again.");
  }

  async function copyMessage(content: string): Promise<boolean> {
    try {
      await navigator.clipboard.writeText(content);
      return true;
    } catch {
      setNotice("Could not access the clipboard. Please copy the text manually.");
      return false;
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

  return (
    <>
      <div className="min-h-screen bg-[#f8f9fc] text-slate-900">
        <input ref={fileInputRef} type="file" accept=".pdf,application/pdf" multiple className="hidden" onChange={handleFileSelection} />

        <div className="flex min-h-screen">
            <div className={`sticky top-0 z-20 flex h-screen w-12 shrink-0 flex-col self-start border-r border-slate-200/80 
            bg-white ${isSidebarCollapsed ? "" : "lg:hidden"}`}>
              <div className="flex h-12 items-center justify-center">
                <SidebarToggleButton onClick={toggleSidebar} />
              </div>
            </div>

          {isMobileSidebarOpen && (
            <button type="button" aria-label="Close sidebar" onClick={() => setIsMobileSidebarOpen(false)}
              className="fixed inset-0 z-30 bg-slate-950/35 backdrop-blur-[1px] lg:hidden"
            />
          )}

          <aside id="app-sidebar" className={`fixed inset-y-0 left-0 z-40 flex w-[248px] max-w-[85vw] shrink-0 flex-col 
          overflow-y-auto border-r border-slate-200/80 bg-white shadow-xl transition-[transform,visibility] duration-200 
          lg:visible lg:static lg:z-auto lg:translate-x-0 lg:overflow-visible lg:shadow-none 
          ${isMobileSidebarOpen ? "translate-x-0" : "invisible -translate-x-full"} ${isSidebarCollapsed ? "lg:hidden" : ""}`}
          >

            <div className="flex h-[76px] items-center gap-5 border-b border-slate-100 px-6">
              <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-violet-600 text-white shadow-sm shadow-violet-200">
                <Icon name="book" size={21} />
              </div>
              
              <div className="w-min">
                <div className="text-[19px] font-semibold tracking-tight text-slate-900">
                  Lexis
                </div>
                <div className="text-[10px] font-medium uppercase tracking-[0.19em] text-slate-400">
                  Knowledge workspace
                </div>
              </div>

              <div className="flex h-12 shrink-0 items-center">
                <SidebarToggleButton onClick={toggleSidebar} />
              </div>
            </div>

            <div className="px-4 pt-7">
              <button onClick={() => { openUploadPicker(); setIsMobileSidebarOpen(false); }}
                className="flex w-full items-center justify-center gap-2 rounded-xl bg-violet-600 px-4 py-3 text-sm 
                font-semibold text-white shadow-sm transition hover:bg-violet-700 focus:outline-none focus:ring-4 focus:ring-violet-100">
                <Icon name="plus" size={18} />Add documents
              </button>
            </div>

{/* Navigation */}

            <nav className="mt-8 space-y-1 px-3">
              <p className="mb-3 px-3 text-[10px] font-semibold uppercase tracking-[0.17em] text-slate-400">
                Workspace
              </p>
              {navItems.map((item) => {
                const active = activeNav === item.label;

                return (
                  <button key={item.label} onClick={() => { setActiveNav(item.label); setIsMobileSidebarOpen(false); }}
                    className={`flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-sm transition ${active
                      ? "bg-violet-50 font-semibold text-violet-700"
                      : "font-medium text-slate-500 hover:bg-slate-50 hover:text-slate-800"
                      }`}
                  >
                    <Icon name={item.icon} size={18} />
                    {item.label}
                  </button>
                );
              })}
            </nav>

{/* Conversation List */}

            <div className="mt-9 min-h-0 px-3">
              <div className="mb-2 flex items-center justify-between px-3">
                <p className="text-[10px] font-semibold uppercase tracking-[0.17em] text-slate-400">Conversations</p>
                <button type="button" onClick={openNewConversationDraft} title="New conversation" aria-label="New conversation"
                  className="flex h-6 w-6 items-center justify-center rounded-md text-slate-400 
                  transition hover:bg-violet-50 hover:text-violet-700">
                  <Icon name="plus" size={15} />
                </button>
              </div>

              <ul className="max-h-64 space-y-0.5 overflow-y-auto pr-1">
                {isLoadingConversations && (
                  <li className="px-3 py-2 text-sm text-slate-400">Loading...</li>
                )}
                {!isLoadingConversations && conversations.length === 0 && (
                  <li className="px-3 py-2 text-sm text-slate-400">No conversations yet</li>
                )}
                {conversations.map((c) => {
                  const isActive = c.id === conversationId && activeNav === "Chat";
                  return (
                    <li key={c.id} className="group relative">
                      {editingConversationId === c.id ? (
                        <div className="flex items-center gap-2.5 rounded-lg bg-violet-50 px-3 py-1.5">
                          <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-violet-100 text-violet-600">
                            <Icon name="message" size={14} />
                          </span>
                          <input
                            autoFocus
                            value={editingTitle}
                            maxLength={255}
                            onChange={(e) => setEditingTitle(e.target.value)}
                            onFocus={(e) => e.currentTarget.select()}
                            onBlur={() => void commitRenameConversation(c)}
                            onKeyDown={(e) => {
                              if (e.key === "Enter") e.currentTarget.blur();          // saves via onBlur
                              if (e.key === "Escape") setEditingConversationId(null); // cancels
                            }}
                            className="min-w-0 flex-1 rounded-md border border-violet-300 bg-white px-2 py-1 text-sm text-slate-700 outline-none focus:ring-2 focus:ring-violet-200"
                          />
                        </div>
                      ) : (
                        <>
                          <button type="button" onClick={() => void selectConversation(c)} onDoubleClick={() => startRenameConversation(c)}
                            title={c.title || "Untitled conversation"} aria-current={isActive ? "true" : undefined}
                            className={`flex w-full items-center gap-2.5 rounded-lg py-2 pl-3 pr-16 text-left text-sm 
                              transition ${isActive ? "bg-violet-50 font-medium text-violet-700" : 
                                "text-slate-600 hover:bg-slate-50 hover:text-slate-800"
                              }`}>
                            
                            <span className="min-w-0 flex-1 truncate">{c.title || "Untitled conversation"}</span>
                          </button>

                          <button type="button" onClick={() => startRenameConversation(c)}
                            aria-label={`Rename ${c.title || "conversation"}`} title="Rename conversation"
                            className="absolute right-8 top-1/2 flex h-7 w-7 -translate-y-1/2 items-center 
                            justify-center rounded-md text-slate-400 opacity-0 transition hover:bg-violet-50 hover:text-violet-700 
                            focus-visible:opacity-100 group-hover:opacity-100">
                            
                            <Icon name="pencil" size={14} />
                          </button>

                          <button type="button" onClick={() => void handleDeleteConversation(c)}
                            aria-label={`Delete ${c.title || "conversation"}`} title="Delete conversation"
                            className="absolute right-1.5 top-1/2 flex h-7 w-7 -translate-y-1/2 items-center 
                            justify-center rounded-md text-slate-400 opacity-0 transition hover:bg-rose-50 hover:text-rose-600 
                            focus-visible:opacity-100 group-hover:opacity-100">
                            
                            <Icon name="trash-2" size={15} />
                          </button>
                        </>
                      )}
                    </li>
                  );
                })}
              </ul>
            </div>

{/* Sidebar bottom section with settings and account info */}
            <div className="mt-auto p-4">
              <button onClick={() => setNotice("Settings will be connected in a later step.")}
                className="mt-3 flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-sm text-slate-500 
                transition hover:bg-slate-100 hover:text-slate-800">
                <Icon name="settings" size={18} />Settings
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
                
                <button aria-label="Account options" onClick={() => setNotice("Account options will be connected later.")}
                  className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100">
                  <Icon name="more" size={18} />
                </button>

              </div>
            </div>
          </aside>

          <main className={`min-w-0 flex-1 px-4 sm:px-6 lg:px-8 
            ${activeNav === "Chat" ? "flex flex-col pb-0 pt-6" : "py-6"}`}>
            
            <div className={`mx-auto max-w-[1440px] justify-between 
              ${activeNav === "Chat" ? "flex w-full flex-1 flex-col" : ""}`}>
              
              {notice && (
                <div role="status" className="mb-5 flex items-start justify-between gap-4 rounded-xl border border-violet-200 bg-violet-50 
                px-4 py-3 text-sm text-violet-800">
                  <span>{notice}</span>
                  <button aria-label="Dismiss notification" onClick={() => setNotice("")} className="shrink-0 text-violet-500 hover:text-violet-800">
                    ×
                  </button>
                </div>
              )}

              {activeNav === "Chat" ? (

                <ChatPanel
                  documents={documents}
                  conversationId={conversationId}
                  conversationTitle={conversationTitle}
                  messages={messages}
                  chatInput={chatInput}
                  setChatInput={setChatInput}
                  isCreatingConversation={isCreatingConversation}
                  isSendingMessage={isSendingMessage}
                  isLoadingConversation={isLoadingConversation}
                  chatEndRef={chatEndRef}
                  selectedDocumentIds={selectedDocumentIds}
                  onToggleDocument={(id, checked) => setSelectedDocumentIds((current) => checked ? [...current.filter((item) => item !== id), id] : current.filter((item) => item !== id))}
                  onUploadDocument={openUploadPicker}
                  isUploading={isUploading}
                  onStartNewConversation={startNewConversation}
                  onSendMessage={handleSendMessage}
                  onStopMessage={stopMessage}
                  onEditMessage={editMessage}
                  onCopyMessage={copyMessage}
                  onRetryMessage={retryMessage}
                />

              ) : (
                <>
                  
                  <div className="mb-8 flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
                    <div>
                      <h2 className="text-[28px] font-semibold tracking-tight text-slate-900 sm:text-[32px]">
                        Good to see you
                      </h2>
                      <p className="mt-2 max-w-xl text-sm leading-6 text-slate-500">
                        Manage your documents and turn your knowledge into
                        answers you can trust.
                      </p>
                    </div>
                    <button
                      onClick={() => setActiveNav("Chat")}
                      className="inline-flex w-fit items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-sm 
                        font-semibold text-slate-700 shadow-sm transition hover:border-violet-200 hover:text-violet-700">
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
                        className="inline-flex items-center justify-center gap-2 rounded-lg border border-slate-200 px-3.5 py-2 text-sm 
                        font-medium text-slate-600 transition hover:border-violet-200 hover:bg-violet-50 hover:text-violet-700">
                        <Icon name="plus" size={16} />
                        Add document
                      </button>
                    </div>

                    <div className="flex flex-col gap-3 border-b border-slate-100 px-5 py-4 sm:flex-row sm:items-center sm:justify-between 
                      sm:px-6">
                      <div className="relative w-full sm:max-w-[300px]">
                        <span className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-400">
                          <Icon name="search" size={17} />
                        </span>
                        <input value={query} onChange={(event) => setQuery(event.target.value)}
                          placeholder="Search documents..." className="w-full rounded-lg border border-slate-200 bg-slate-50/70 py-2.5 
                          pl-10 pr-3 text-sm outline-none transition placeholder:text-slate-400 focus:border-violet-300 focus:bg-white 
                          focus:ring-4 focus:ring-violet-50"
                        />
                      </div>
                      <div className="flex flex-wrap items-center gap-1.5">
                        {(
                          ["All", "Pending", "Processing", "Chunked", "Indexed", "Failed"] as const
                        ).map((item) => (
                          <button key={item} onClick={() => setFilter(item)} className={`rounded-lg px-3 py-2 text-xs font-medium 
                            transition ${filter === item ? "bg-violet-50 text-violet-700" : "text-slate-500 hover:bg-slate-50 hover:text-slate-800"
                              }`}>
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
                            <tr key={document.id} className="transition hover:bg-slate-50/70">
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
                              <td className="px-4 py-4 text-sm text-slate-500">{document.status}</td>
                              <td className="px-4 py-4 text-sm text-slate-500">{document.size}</td>
                              <td className="px-4 py-4 text-sm text-slate-500">{document.updated}</td>
                              <td className="px-5 py-4 text-right">

                                <div className="relative inline-flex">
                                  <button type="button" onClick={(event) => {
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
                                      <button type="button" onClick={() => void handleViewDetails(document)}
                                        disabled={isLoadingDetails} className="flex w-full items-center gap-2 rounded-lg px-3 py-2 
                                          text-left text-sm text-slate-700 hover:bg-slate-50 disabled:opacity-50">
                                        <Icon name="file" size={16} />
                                        View details
                                      </button>

                                      <button type="button" onClick={() => void handleDownload(document)}
                                        className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-sm text-slate-700 
                                        hover:bg-slate-50">
                                        <Icon name="download" size={16} />
                                        Download
                                      </button>

                                      <button type="button" onClick={() => void handleDelete(document)}
                                        className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-sm text-rose-600 hover:bg-rose-50">
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
                        Showing {filteredDocuments.length} of {documents.length}{" "}documents                      
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
                          Ask questions and explore answers grounded in your documents, with sources you can trace back to the original content.
                        </p>
                      </div>
                      <button
                        onClick={() => setActiveNav("Chat")}
                        className="inline-flex shrink-0 items-center gap-2 rounded-xl bg-white px-4 py-3 text-sm font-semibold text-slate-900 
                        transition hover:bg-violet-50">
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
          <div className="fixed inset-0 z-[10000] flex items-center justify-center bg-slate-950/40 p-4" onClick={() => setSelectedDocument(null)}>
            <div role="dialog" aria-modal="true" aria-labelledby="document-details-title"
              className="w-full max-w-lg rounded-2xl bg-white p-6 shadow-2xl" onClick={(event) => event.stopPropagation()}>
              <div className="flex items-start justify-between gap-4">
                <div className="flex items-center gap-3">
                  <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-violet-50 text-violet-600">
                    <Icon name="file" size={22} />
                  </div>

                  <div className="min-w-0">
                    <h2 id="document-details-title" className="text-lg font-semibold text-slate-900">
                      Document details
                    </h2>
                    <p className="mt-1 break-all text-sm text-slate-500">
                      {selectedDocument.name}
                    </p>
                  </div>
                </div>

                <button type="button" onClick={() => setSelectedDocument(null)}
                  aria-label="Close document details"
                  className="rounded-lg px-2 py-1 text-xl text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                >
                  ×
                </button>
              </div>

              <div className="mt-6 divide-y divide-slate-100">
                <div className="flex items-center justify-between gap-4 py-3">
                  <span className="text-sm text-slate-500">Status</span>
                  <span className="text-right text-sm font-medium text-slate-800">
                    {selectedDocument.status}
                  </span>
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
