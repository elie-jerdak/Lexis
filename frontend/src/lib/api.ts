
const API_BASE_URL =
    process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5238";

const ACCESS_TOKEN_KEY = "lexis_access_token";
const REFRESH_TOKEN_KEY = "lexis_refresh_token";
const TOKEN_EXPIRATION_KEY = "lexis_access_token_expiration";

export interface AuthResponse {
    accessToken: string;
    refreshToken: string;
    accessTokenExpiration: string;
    message: string;
}

export interface ApiErrorBody {
    message?: string;
    error?: string;
    code?: string;
}

export function saveAuth(auth: AuthResponse): void {
    sessionStorage.setItem(ACCESS_TOKEN_KEY, auth.accessToken);
    sessionStorage.setItem(REFRESH_TOKEN_KEY, auth.refreshToken);
    sessionStorage.setItem(
        TOKEN_EXPIRATION_KEY,
        auth.accessTokenExpiration,
    );
}

export function getAccessToken(): string | null {
    if (typeof window === "undefined") return null;
    return sessionStorage.getItem(ACCESS_TOKEN_KEY);
}

export function clearAuth(): void {
    sessionStorage.removeItem(ACCESS_TOKEN_KEY);
    sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    sessionStorage.removeItem(TOKEN_EXPIRATION_KEY);
}

export function getApiErrorMessage(
    body: ApiErrorBody | null,
    fallback: string,
): string {
    return body?.message ?? body?.error ?? fallback;
}

export async function apiRequest<T>(
    path: string,
    options: RequestInit = {},
    authenticated = true,
): Promise<T> {
    const headers = new Headers(options.headers);

    if (
        options.body &&
        !(options.body instanceof FormData) &&
        !headers.has("Content-Type")
    ) {
        headers.set("Content-Type", "application/json");
    }

    if (authenticated) {
        const token = getAccessToken();

        if (token) {
            headers.set("Authorization", `Bearer ${token}`);
        }
    }

    const response = await fetch(`${API_BASE_URL}${path}`, {
        ...options,
        headers,
        cache: "no-store",
    });

    if (!response.ok) {
        const body = (await response.json().catch(() => null)) as
            | ApiErrorBody
            | null;

        if (response.status === 401 && authenticated) {
            clearAuth();
        }

        throw new Error(
            getApiErrorMessage(
                body,
                `Request failed (${response.status}).`,
            ),
        );
    }

    if (response.status === 204) {
        return undefined as T;
    }

    return (await response.json()) as T;
}

export async function login(
    email: string,
    password: string,
): Promise<AuthResponse> {
    const auth = await apiRequest<AuthResponse>(
        "/api/Auth/login",
        {
            method: "POST",
            body: JSON.stringify({ email, password }),
        },
        false,
    );

    saveAuth(auth);
    return auth;
}
