// Commit messages follow Conventional Commits (https://www.conventionalcommits.org).
// Checked by the husky commit-msg hook and by the "Conventional commits" CI check.
// Structure (type, scope, separator, description) is an error; style is a warning.
export default {
  extends: ['@commitlint/config-conventional'],
  rules: {
    // Lowercase subjects are house style, not part of the spec. Dependabot capitalizes "Bump".
    'subject-case': [1, 'never', ['sentence-case', 'start-case', 'pascal-case', 'upper-case']],
    // Dependabot titles with long NuGet IDs and versions can run past 100 characters.
    'header-max-length': [1, 'always', 100],
    // Bodies and footers may carry long URLs (Dependabot release notes, links to docs).
    'body-max-line-length': [0],
    'footer-max-line-length': [0],
  },
};
