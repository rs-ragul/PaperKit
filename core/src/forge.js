/**
 * node-forge resolver.
 *
 * In Node / a bundler we import the package. In a browser that already has
 * the UMD global (dev server mode) we prefer that instance. The same core
 * code therefore runs unmodified everywhere.
 */
import imported from 'node-forge';

const forge = globalThis.forge || imported;

export default forge;
