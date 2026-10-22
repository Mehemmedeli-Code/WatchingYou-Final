import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "@/lib/utils";

const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-full text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent disabled:pointer-events-none disabled:opacity-45",
  {
    variants: {
      variant: {
        solid: "bg-accent text-surface hover:bg-accent-bright",
        outline: "border border-line text-ink hover:border-accent hover:text-accent",
        ghost: "text-ink-mute hover:text-ink",
        danger: "border border-bad-bg text-bad hover:bg-bad-bg/50",
      },
      size: {
        sm: "h-8 px-3",
        md: "h-10 px-5",
        lg: "h-12 px-7 text-base",
      },
    },
    defaultVariants: { variant: "solid", size: "md" },
  },
);

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, type = "button", ...props }, ref) => (
    <button ref={ref} type={type} className={cn(buttonVariants({ variant, size }), className)} {...props} />
  ),
);
Button.displayName = "Button";

export { buttonVariants };
