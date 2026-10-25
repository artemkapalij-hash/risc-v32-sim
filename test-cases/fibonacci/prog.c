__attribute__((noinline))
int fib(int n) {
    if (n < 2)
        return n;
    return fib(n - 1) + fib(n - 2);
}

int main(void) {
    volatile int n = 10;
    return fib(n);
}
