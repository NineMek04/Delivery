export interface HttpStatusResult {
    Success: boolean;
    Message: string;
    ErrorDetail?: string;
    Code?: string;
}

export interface HttpStatusResultValue<T> extends HttpStatusResult {
    Value: T;
}
