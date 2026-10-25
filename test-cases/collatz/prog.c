__attribute__((noinline))
int collatz(unsigned n) {
    int steps = 0;
    while (n != 1) {
        n = (n & 1) ? 3 * n + 1 : n >> 1;
        steps++;
    }
    return steps;
}

int main(void) {
    volatile unsigned n = 27;
    return collatz(n);
}
