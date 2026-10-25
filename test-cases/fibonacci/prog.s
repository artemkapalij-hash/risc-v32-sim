	.file	"prog.c"
	.option nopic
	.attribute arch, "rv32i2p1_m2p0_zmmul1p0"
	.attribute unaligned_access, 0
	.attribute stack_align, 16
	.text
	.align	2
	.globl	fib
	.type	fib, @function
fib:
	addi	sp,sp,-16
	sw	ra,12(sp)
	sw	s0,8(sp)
	mv	s0,a0
	li	a5,1
	ble	a0,a5,.L1
	sw	s1,4(sp)
	addi	a0,a0,-1
	call	fib
	mv	s1,a0
	addi	a0,s0,-2
	call	fib
	add	a0,s1,a0
	lw	s1,4(sp)
.L1:
	lw	ra,12(sp)
	lw	s0,8(sp)
	addi	sp,sp,16
	jr	ra
	.size	fib, .-fib
	.align	2
	.globl	main
	.type	main, @function
main:
	addi	sp,sp,-32
	sw	ra,28(sp)
	li	a5,10
	sw	a5,12(sp)
	lw	a0,12(sp)
	call	fib
	lw	ra,28(sp)
	addi	sp,sp,32
	jr	ra
	.size	main, .-main
	.section	.note.GNU-stack,"",@progbits
