	.file	"prog.c"
	.option nopic
	.attribute arch, "rv32i2p1_m2p0_zmmul1p0"
	.attribute unaligned_access, 0
	.attribute stack_align, 16
	.text
	.align	2
	.globl	gcd
	.type	gcd, @function
gcd:
	mv	a5,a0
	mv	a0,a1
	bne	a5,a1,.L5
.L2:
	ret
.L3:
	sub	a0,a0,a5
.L4:
	beq	a5,a0,.L2
.L5:
	ble	a5,a0,.L3
	sub	a5,a5,a0
	j	.L4
	.size	gcd, .-gcd
	.align	2
	.globl	main
	.type	main, @function
main:
	addi	sp,sp,-32
	sw	ra,28(sp)
	li	a5,1071
	sw	a5,12(sp)
	li	a5,462
	sw	a5,8(sp)
	lw	a0,12(sp)
	lw	a1,8(sp)
	call	gcd
	lw	ra,28(sp)
	addi	sp,sp,32
	jr	ra
	.size	main, .-main
	.section	.note.GNU-stack,"",@progbits
