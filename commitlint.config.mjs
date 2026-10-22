// Commit messages follow Conventional Commits (https://www.conventionalcommits.org).
// Checked by the husky commit-msg hook and by the "Conventional commits" CI check.
export default {
  extends: ['@commitlint/config-conventional'],
  rules: {
    // Bodies and footers may carry long URLs (Dependabot release notes, links to docs).
    'body-max-line-length': [0],
    'footer-max-line-length': [0],
  },
};
