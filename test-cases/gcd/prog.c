__attribute__((noinline))
int gcd(int a, int b) {
    while (a != b) {
        if (a > b)
            a -= b;
        else
            b -= a;
    }
    return a;
}

int main(void) {
    volatile int a = 1071;
    volatile int b = 462;
    return gcd(a, b);
}
