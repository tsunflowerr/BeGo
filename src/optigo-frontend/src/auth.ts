import NextAuth from "next-auth";
import Google from "next-auth/providers/google";

export const { handlers, auth, signIn, signOut } = NextAuth({
  providers: [
    Google({
      clientId: process.env.AUTH_GOOGLE_ID,
      clientSecret: process.env.AUTH_GOOGLE_SECRET,
      authorization: {
        params: {
          scope: "openid email profile",
          prompt: "select_account",
        },
      },
      allowDangerousEmailAccountLinking: true,
    }),
  ],
  trustHost: true,
  callbacks: {
    async jwt({ token, account }) {
      if (account?.id_token) {
        token.idToken = account.id_token;
      }
      return token;
    },
    async session({ session, token }) {
      (session as typeof session & { idToken?: string }).idToken = typeof token.idToken === "string" ? token.idToken : undefined;
      return session;
    },
  },
});
