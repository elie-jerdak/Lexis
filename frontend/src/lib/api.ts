
const API_BASE_URL =
    process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5238";

const REFRESH_PATH = "/api/Auth/refresh";

export interface LoginResponse {
    message: string;
}

export interface RegistrationResponse {
    message: string;
}

export interface RegisterRequest {
    firstName: string;
    lastName: string;
    email: string;
    phoneNumber: string;
    password: string;
}

export interface ApiErrorBody {
    message?: string;
    error?: string;
    code?: string;
}

let refreshPromise: Promise<boolean> | null = null;

export function clearAuth(): void {
    // Authentication tokens are HttpOnly cookies managed by the API.
    // JavaScript must not attempt to read or delete them.
}

export function getApiErrorMessage(
    body: ApiErrorBody | null,
    fallback: string,
): string {
    return body?.message ?? body?.error ?? fallback;
}

async function readError(response: Response): Promise<Error> {
    const body = (await response.json().catch(() => null)) as
        | ApiErrorBody
        | null;

    return new Error(
        getApiErrorMessage(body, `Request failed (${response.status}).`),
    );
}

async function refreshAccessToken(): Promise<boolean> {
    if (refreshPromise) return refreshPromise;

    refreshPromise = (async () => {
        try {
            const response = await fetch(
                `${API_BASE_URL}${REFRESH_PATH}`,
                {
                    method: "POST",
                    credentials: "include",
                    cache: "no-store",
                },
            );

            return response.ok;
        } catch {
            // A network failure doesn't prove the session is invalid.
            return false;
        } finally {
            refreshPromise = null;
        }
    })();

    return refreshPromise;
}

async function sendRequest(
    path: string,
    options: RequestInit,
): Promise<Response> {
    const headers = new Headers(options.headers);

    if (
        options.body &&
        typeof FormData !== "undefined" &&
        !(options.body instanceof FormData) &&
        !headers.has("Content-Type")
    ) {
        headers.set("Content-Type", "application/json");
    }

    // No Authorization header: the API reads the HttpOnly cookie.
    headers.delete("Authorization");

    return fetch(`${API_BASE_URL}${path}`, {
        ...options,
        headers,
        credentials: "include",
        cache: "no-store",
    });
}

export async function apiRequest<T>(
    path: string,
    options: RequestInit = {},
    authenticated = true,
): Promise<T> {
    let response = await sendRequest(path, options);

    if (response.status === 401 && authenticated) {
        const refreshed = await refreshAccessToken();

        if (refreshed) {
            // Retry the original request once, never indefinitely.
            response = await sendRequest(path, options);
        }
    }

    if (!response.ok) {
        throw await readError(response);
    }

    if (response.status === 204) {
        return undefined as T;
    }

    return (await response.json()) as T;
}

export async function login(
    email: string,
    password: string,
): Promise<LoginResponse> {
    return apiRequest<LoginResponse>(
        "/api/Auth/login",
        {
            method: "POST",
            body: JSON.stringify({ email, password }),
        },
        false,
    );
}

export async function register(
    request: RegisterRequest,
): Promise<RegistrationResponse> {
    return apiRequest<RegistrationResponse>(
        "/api/Auth/register",
        {
            method: "POST",
            body: JSON.stringify(request),
        },
        false,
    );
}

export async function authenticatedFetch(
    path: string,
    options: RequestInit = {},
): Promise<Response> {
    let response = await sendRequest(path, options);

    if (response.status === 401) {
        const refreshed = await refreshAccessToken();

        if (refreshed) {
            response = await sendRequest(path, options);
        }
    }

    return response;
}

export async function logout(): Promise<void> {
    try {
        await fetch(`${API_BASE_URL}/api/Auth/logout`, {
            method: "POST",
            credentials: "include",
            cache: "no-store",
        });
    } catch {
        // The API may be unavailable. The caller can still navigate
        // away, but server-side revocation cannot be guaranteed.
    }
}
