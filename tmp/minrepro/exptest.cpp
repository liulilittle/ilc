#include <cmath>
#include <cstdio>
int main() {
    double e = std::exp(1.0);
    printf("%.17g\n", e);
    printf("gt=%d lt=%d\n", e > 2.718281828459045, e < 2.718281828459046);
    return 0;
}