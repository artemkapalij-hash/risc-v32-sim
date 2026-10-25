	.file	"prog.c"
	.option nopic
	.attribute arch, "rv32i2p1_m2p0_zmmul1p0"
	.attribute unaligned_access, 0
	.attribute stack_align, 16
	.text
	.align	2
	.globl	collatz
	.type	collatz, @function
collatz:
	mv	a5,a0
	li	a4,1
	beq	a0,a4,.L6
	li	a0,0
	mv	a3,a4
	j	.L5
.L3:
	srli	a5,a5,1
	addi	a0,a0,1
	beq	a5,a3,.L7
.L5:
	andi	a4,a5,1
	beq	a4,zero,.L3
	slli	a4,a5,1
	add	a5,a4,a5
	addi	a5,a5,1
	addi	a0,a0,1
	j	.L5
.L7:
	ret
.L6:
	li	a0,0
	ret
	.size	collatz, .-collatz
	.align	2
	.globl	main
	.type	main, @function
main:
	addi	sp,sp,-32
	sw	ra,28(sp)
	li	a5,27
	sw	a5,12(sp)
	lw	a0,12(sp)
	call	collatz
	lw	ra,28(sp)
	addi	sp,sp,32
	jr	ra
	.size	main, .-main
	.section	.note.GNU-stack,"",@progbits
